namespace Dovetail.Example.Business;

public record ShipmentTrackingInfo(
    bool HasShipped,
    string? Carrier,
    string? TrackingNumber,
    DateTimeOffset? EstimatedDelivery
);
