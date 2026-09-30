using System.Text.Json.Serialization;
using DeliveryTracking.Api;
using DeliveryTracking.Api.Infrastructure;
using DeliveryTracking.Application;
using DeliveryTracking.Application.InMemory;
using DeliveryTracking.Domain;

var builder = WebApplication.CreateBuilder(args);

var policy = builder.Configuration.GetSection("Tracking").Get<TrackingPolicy>() ?? TrackingPolicy.Default;
policy.EnsureValid();

builder.Services.AddSingleton(policy);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IDeliveryRepository, InMemoryDeliveryRepository>();
builder.Services.AddSingleton<IRouteProvider, StraightLineRouteProvider>();
builder.Services.AddSingleton<TrackingReadModel>();
builder.Services.AddSingleton<RerouteQueue>();
builder.Services.AddSingleton<ITrackingEventSink, TrackingEventDispatcher>();
builder.Services.AddSingleton<TrackingService>();
builder.Services.AddHostedService<RerouteWorker>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<TrackingExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.UseExceptionHandler();
app.MapInternalEndpoints();
app.MapCourierEndpoints();
app.MapCustomerEndpoints();

app.Run();

public partial class Program;
