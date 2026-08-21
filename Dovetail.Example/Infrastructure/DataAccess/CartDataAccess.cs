namespace Dovetail.Example.Infrastructure;

internal class CartDataAccess
{
    private static readonly Dictionary<(int UserId, int CartId), List<CartItemRecord>> Carts = new()
    {
        [(1, 1)] =
        [
            new CartItemRecord(1, "Trail Running Shoes", 1, 128.00m),
            new CartItemRecord(2, "Wireless Earbuds", 1, 89.00m),
            new CartItemRecord(5, "Merino Wool Socks (3-Pack)", 2, 24.00m),
        ],
        [(2, 2)] = [],
    };

    public async Task<CartRecord> GetCartAsync(int userId, int cartId, CancellationToken ct)
    {
        await SimulatedLatency.Delay(ct);

        return Carts.TryGetValue((userId, cartId), out var items)
            ? new CartRecord(items)
            : throw new KeyNotFoundException($"No cart {cartId} for user {userId}.");
    }
}
