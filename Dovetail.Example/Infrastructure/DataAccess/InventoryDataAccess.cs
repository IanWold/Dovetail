namespace Dovetail.Example.Infrastructure;

internal class InventoryDataAccess
{
    private static readonly Dictionary<int, int> UnitsInStock = new()
    {
        [1] = 42,
        [2] = 0,
        [3] = 15,
        [4] = 8,
        [5] = 120,
        [6] = 3,
        // Sku 7 is intentionally missing - GetUnitsInStockAsync throws for it below.
        [8] = 20,
    };

    public async Task<int?> GetUnitsInStockAsync(int sku, CancellationToken ct)
    {
        await SimulatedLatency.Delay(ct);

        if (sku == 7)
        {
            throw new TimeoutException("Warehouse inventory feed timed out.");
        }

        return UnitsInStock.TryGetValue(sku, out var units) ? units : null;
    }
}
