namespace Dovetail.Example.Infrastructure;

internal class ShipmentTrackingDataAccess
{
    public async Task<ShipmentTrackingRecord> GetTrackingAsync(int orderId, CancellationToken ct)
    {
        await SimulatedLatency.Delay(ct);

        return orderId switch
        {
            1 => new ShipmentTrackingRecord(true, "ParcelForce", "PF123456789US", DateTimeOffset.UtcNow.AddDays(-2)),
            _ => new ShipmentTrackingRecord(false, null, null, null)
        };
    }
}
