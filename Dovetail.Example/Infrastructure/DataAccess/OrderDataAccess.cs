namespace Dovetail.Example.Infrastructure;

internal class OrderDataAccess
{
    private static readonly Dictionary<int, OrderRecord> Orders = new()
    {
        [1] = new OrderRecord(
            1,
            [
                new CartItemRecord(1, "Trail Running Shoes", 1, 128.00m),
                new CartItemRecord(4, "Ultralight Rain Jacket", 1, 210.00m),
            ],
            338.00m,
            DateTimeOffset.UtcNow.AddDays(-6)
        ),
        [2] = new OrderRecord(
            2,
            [
                new CartItemRecord(6, "Foldable Trekking Poles", 1, 64.00m),
            ],
            64.00m,
            DateTimeOffset.UtcNow.AddDays(-1)
        ),
    };

    public async Task<OrderRecord> GetOrderAsync(int orderId, CancellationToken ct)
    {
        await SimulatedLatency.Delay(ct);

        return Orders.TryGetValue(orderId, out var order)
            ? order
            : throw new KeyNotFoundException($"No order with ID {orderId}.");
    }
}
