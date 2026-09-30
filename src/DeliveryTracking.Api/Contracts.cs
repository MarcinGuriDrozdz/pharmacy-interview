using DeliveryTracking.Application;

namespace DeliveryTracking.Api;

public sealed record CoordinatesDto(double Latitude, double Longitude);

public sealed record PharmacyDto(Guid Id, string Name, string Address, double Latitude, double Longitude);

/// <summary>Stand-in for the <c>PharmacyAssigned</c> integration event published by Fulfillment.</summary>
public sealed record RegisterDeliveryRequest(
    Guid DeliveryId,
    Guid OrderId,
    Guid CustomerId,
    PharmacyDto Pharmacy,
    CoordinatesDto Destination);

/// <summary>Stand-in for the <c>ParcelPickedUp</c> event from the courier app / dispatch.</summary>
public sealed record PickUpRequest(Guid CourierId, DateTimeOffset PickedUpAt);

public sealed record LocationFixDto(
    Guid EventId,
    long Sequence,
    DateTimeOffset RecordedAt,
    double Latitude,
    double Longitude);

public sealed record LocationBatchRequest(IReadOnlyList<LocationFixDto> Fixes);

public sealed record LocationBatchResponse(IReadOnlyList<FixOutcome> Results);
