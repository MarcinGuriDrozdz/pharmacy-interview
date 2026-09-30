using System.Net;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeliveryTracking.Api;
using DeliveryTracking.Application;
using DeliveryTracking.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using static DeliveryTracking.Tests.TestKit;

namespace DeliveryTracking.Tests;

public class TrackingApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task Customer_follows_the_delivery_live_from_pharmacy_assignment_to_movement()
    {
        using var client = factory.CreateClient();
        var order = await Register(client);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/orders/{order.OrderId}/tracking/stream");
        request.Headers.Add(Endpoints.CustomerHeader, order.CustomerId.ToString());
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
        await using var events = SseParser.Create(body).EnumerateAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);

        var assigned = await Next(events);
        Assert.Equal(DeliveryPhase.AwaitingPickup, assigned.Phase);
        Assert.Equal(Pharmacy.Name, assigned.Pharmacy.Name);
        Assert.Null(assigned.Courier);

        var courierId = Guid.NewGuid();
        var pickup = await client.PostAsJsonAsync($"/internal/deliveries/{order.DeliveryId}/pickup",
            new PickUpRequest(courierId, DateTimeOffset.UtcNow.AddSeconds(-30)), timeout.Token);
        Assert.Equal(HttpStatusCode.NoContent, pickup.StatusCode);

        var pickedUp = await Next(events);
        Assert.Equal(DeliveryPhase.InTransit, pickedUp.Phase);
        Assert.NotNull(pickedUp.Route);
        Assert.Equal(0, pickedUp.Courier!.Progress);

        var outcome = await ReportAsync(client, order.DeliveryId, courierId, eastMeters: 400, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, outcome.StatusCode);

        var moving = await Next(events);
        Assert.True(moving.Version > pickedUp.Version);
        Assert.InRange(moving.Courier!.Progress, 0.35, 0.45);
        Assert.Null(moving.Route);
    }

    [Fact]
    public async Task Tracking_is_visible_only_to_the_ordering_customer()
    {
        using var client = factory.CreateClient();
        var order = await Register(client);

        var anonymous = await client.GetAsync($"/orders/{order.OrderId}/tracking");
        var stranger = await GetTracking(client, order.OrderId, Guid.NewGuid());
        var owner = await GetTracking(client, order.OrderId, order.CustomerId);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
    }

    [Fact]
    public async Task Courier_endpoints_reject_foreign_couriers_and_bad_coordinates()
    {
        using var client = factory.CreateClient();
        var order = await Register(client);
        var courierId = Guid.NewGuid();
        await client.PostAsJsonAsync($"/internal/deliveries/{order.DeliveryId}/pickup",
            new PickUpRequest(courierId, DateTimeOffset.UtcNow.AddSeconds(-30)));

        var foreign = await ReportAsync(client, order.DeliveryId, Guid.NewGuid(), eastMeters: 100, default);
        var invalid = await ReportAsync(client, order.DeliveryId, courierId, eastMeters: 100, default, latitude: 123);
        var unknown = await ReportAsync(client, Guid.NewGuid(), courierId, eastMeters: 100, default);
        var secondCourier = await client.PostAsJsonAsync($"/internal/deliveries/{order.DeliveryId}/pickup",
            new PickUpRequest(Guid.NewGuid(), DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondCourier.StatusCode);
    }

    private static async Task<(Guid DeliveryId, Guid OrderId, Guid CustomerId)> Register(HttpClient client)
    {
        var order = (DeliveryId: Guid.NewGuid(), OrderId: Guid.NewGuid(), CustomerId: Guid.NewGuid());
        var destination = East(1000);
        var response = await client.PostAsJsonAsync("/internal/deliveries", new RegisterDeliveryRequest(
            order.DeliveryId,
            order.OrderId,
            order.CustomerId,
            new PharmacyDto(Pharmacy.Id, Pharmacy.Name, Pharmacy.Address, Origin.Latitude, Origin.Longitude),
            new CoordinatesDto(destination.Latitude, destination.Longitude)));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return order;
    }

    private static Task<HttpResponseMessage> GetTracking(HttpClient client, Guid orderId, Guid customerId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/orders/{orderId}/tracking");
        request.Headers.Add(Endpoints.CustomerHeader, customerId.ToString());
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> ReportAsync(
        HttpClient client,
        Guid deliveryId,
        Guid courierId,
        double eastMeters,
        CancellationToken ct,
        double? latitude = null)
    {
        var point = East(eastMeters);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/courier/deliveries/{deliveryId}/locations")
        {
            Content = JsonContent.Create(new LocationBatchRequest(
            [
                new LocationFixDto(Guid.NewGuid(), 1, DateTimeOffset.UtcNow, latitude ?? point.Latitude, point.Longitude),
            ])),
        };
        request.Headers.Add(Endpoints.CourierHeader, courierId.ToString());
        return client.SendAsync(request, ct);
    }

    private static async Task<TrackingView> Next(IAsyncEnumerator<SseItem<string>> events)
    {
        Assert.True(await events.MoveNextAsync(), "The tracking stream ended early.");
        Assert.Equal("tracking", events.Current.EventType);
        return JsonSerializer.Deserialize<TrackingView>(events.Current.Data, Json)!;
    }
}
