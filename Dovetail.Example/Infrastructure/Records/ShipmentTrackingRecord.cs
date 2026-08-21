namespace Dovetail.Example.Infrastructure;

internal record ShipmentTrackingRecord(
    bool HasShipped,
    string? Carrier,
    string? TrackingNumber,
    DateTimeOffset? EstimatedDelivery
);
