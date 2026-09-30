namespace DeliveryTracking.Application;

public sealed class DeliveryNotFoundException(Guid deliveryId)
    : Exception($"Delivery {deliveryId} was not found.")
{
    public Guid DeliveryId { get; } = deliveryId;
}

public sealed class CourierNotAssignedException(Guid deliveryId, Guid courierId)
    : Exception($"Courier {courierId} is not assigned to delivery {deliveryId}.");
