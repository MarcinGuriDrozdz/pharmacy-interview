using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DeliveryTracking.Application;
using DeliveryTracking.Domain;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace DeliveryTracking.Api;

public static class Endpoints
{
    /// <summary>
    /// Development stand-ins for authentication. In production the ids come from the JWT <c>sub</c> claim
    /// of the customer app / courier app token, and <c>/internal</c> is reachable only from the service mesh.
    /// </summary>
    public const string CustomerHeader = "X-Customer-Id";
    public const string CourierHeader = "X-Courier-Id";

    /// <summary>Integration-event consumers exposed over HTTP so the module can be driven without a broker.</summary>
    public static void MapInternalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/deliveries");

        group.MapPost("/", async (RegisterDeliveryRequest request, TrackingService tracking, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request.Pharmacy);
            ArgumentNullException.ThrowIfNull(request.Destination);
            var created = await tracking.RegisterAsync(
                new RegisterDelivery(
                    request.DeliveryId,
                    request.OrderId,
                    request.CustomerId,
                    new PharmacyInfo(
                        request.Pharmacy.Id,
                        request.Pharmacy.Name,
                        request.Pharmacy.Address,
                        new GeoPoint(request.Pharmacy.Latitude, request.Pharmacy.Longitude)),
                    new GeoPoint(request.Destination.Latitude, request.Destination.Longitude)),
                ct);

            return created
                ? Results.Created($"/orders/{request.OrderId}/tracking", null)
                : Results.Ok();
        });

        group.MapPost("/{deliveryId:guid}/pickup", async (Guid deliveryId, PickUpRequest request, TrackingService tracking, CancellationToken ct) =>
        {
            await tracking.PickUpAsync(deliveryId, request.CourierId, request.PickedUpAt, ct);
            return Results.NoContent();
        });
    }

    public static void MapCourierEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/courier/deliveries/{deliveryId:guid}/locations", async (
            Guid deliveryId,
            LocationBatchRequest request,
            HttpContext http,
            TrackingService tracking,
            CancellationToken ct) =>
        {
            if (!TryReadId(http, CourierHeader, out var courierId))
            {
                return Results.Unauthorized();
            }

            ArgumentNullException.ThrowIfNull(request.Fixes);
            var fixes = request.Fixes
                .Select(f => new LocationFix(f.EventId, f.Sequence, f.RecordedAt, new GeoPoint(f.Latitude, f.Longitude)))
                .ToArray();

            var outcomes = await tracking.ReportLocationsAsync(deliveryId, courierId, fixes, ct);
            return Results.Ok(new LocationBatchResponse(outcomes));
        });
    }

    public static void MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/orders/{orderId:guid}/tracking");

        group.MapGet("/", (Guid orderId, HttpContext http, TrackingReadModel readModel) =>
        {
            if (!TryReadId(http, CustomerHeader, out var customerId))
            {
                return Results.Unauthorized();
            }

            var view = readModel.Find(orderId);

            // Someone else's order looks exactly like a missing one.
            return view is null || view.CustomerId != customerId ? Results.NotFound() : Results.Ok(view);
        });

        group.MapGet("/stream", (
            Guid orderId,
            HttpContext http,
            TrackingReadModel readModel,
            IOptions<JsonOptions> json,
            CancellationToken ct) =>
        {
            if (!TryReadId(http, CustomerHeader, out var customerId))
            {
                return Results.Unauthorized();
            }

            var subscription = readModel.Subscribe(orderId);
            var current = readModel.Find(orderId);
            if (current is null || current.CustomerId != customerId)
            {
                subscription.Dispose();
                return Results.NotFound();
            }

            return TypedResults.ServerSentEvents(Stream(subscription, current, json.Value.SerializerOptions, ct));
        });
    }

    /// <summary>
    /// First event is the full current state, then every newer state. The route polyline is sent only when it changed.
    /// The stream ends after arrival. A reconnecting client simply gets the current state again.
    /// </summary>
    private static async IAsyncEnumerable<SseItem<string>> Stream(
        TrackingReadModel.Subscription subscription,
        TrackingView current,
        JsonSerializerOptions options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using (subscription)
        {
            yield return ToItem(current, options);
            var lastVersion = current.Version;
            var lastRoute = current.RouteVersion;
            if (current.Phase == DeliveryPhase.Arrived)
            {
                yield break;
            }

            await foreach (var view in subscription.Reader.ReadAllAsync(ct))
            {
                if (view.Version <= lastVersion)
                {
                    continue;
                }

                yield return ToItem(view.RouteVersion == lastRoute ? view with { Route = null } : view, options);
                lastVersion = view.Version;
                lastRoute = view.RouteVersion;
                if (view.Phase == DeliveryPhase.Arrived)
                {
                    yield break;
                }
            }
        }
    }

    private static SseItem<string> ToItem(TrackingView view, JsonSerializerOptions options)
        => new(JsonSerializer.Serialize(view, options), "tracking")
        {
            EventId = view.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

    private static bool TryReadId(HttpContext http, string header, out Guid id)
        => Guid.TryParse(http.Request.Headers[header], out id) && id != Guid.Empty;
}
