using Dovetail.Example.Infrastructure;

namespace Dovetail.Example.Business;

// Sku 7's warehouse feed always times out. Rather than fail the whole product
// page over a missing stock count, this catches it and degrades to "Unknown" —
// the pattern documented in the README under Architectural Considerations.
// The stock-level thresholds (what counts as "low") are a business rule, applied
// here on top of the raw unit count Infrastructure returns.
internal class InventorySegment(InventoryDataAccess inventory) : IPipelineSegment<Sku, InventoryStatus>
{
    public async Task<InventoryStatus> ExecuteAsync(Sku sku, CancellationToken ct)
    {
        try
        {
            var units = await inventory.GetUnitsInStockAsync(sku.Value, ct);
            return units switch
            {
                null => new InventoryStatus(StockLevel.Unknown, null),
                0 => new InventoryStatus(StockLevel.OutOfStock, 0),
                < 5 => new InventoryStatus(StockLevel.LowStock, units),
                _ => new InventoryStatus(StockLevel.InStock, units)
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new InventoryStatus(StockLevel.Unknown, null);
        }
    }
}
