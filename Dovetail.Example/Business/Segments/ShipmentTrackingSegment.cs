using Dovetail.Example.Infrastructure;

namespace Dovetail.Example.Business;

internal class ShipmentTrackingSegment(ShipmentTrackingDataAccess tracking) : IPipelineSegment<OrderDetails, ShipmentTrackingInfo>
{
    public async Task<ShipmentTrackingInfo> ExecuteAsync(OrderDetails order, CancellationToken ct)
    {
        var record = await tracking.GetTrackingAsync(order.OrderId.Value, ct);
        return new ShipmentTrackingInfo(record.HasShipped, record.Carrier, record.TrackingNumber, record.EstimatedDelivery);
    }
}
