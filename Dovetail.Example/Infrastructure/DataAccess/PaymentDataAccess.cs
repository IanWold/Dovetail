namespace Dovetail.Example.Infrastructure;

internal class PaymentDataAccess
{
    public async Task<PaymentRecord> GetStatusAsync(int orderId, CancellationToken ct)
    {
        await SimulatedLatency.Delay(ct);

        return new PaymentRecord("PAID", $"txn_{orderId:D6}");
    }
}
