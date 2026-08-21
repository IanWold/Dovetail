namespace Dovetail.Example.Infrastructure;

internal class ProductCatalogDataAccess
{
    private static readonly Dictionary<int, ProductRecord> Products = new()
    {
        [1] = new ProductRecord(1, "Trail Running Shoes", "Grippy, lightweight shoes for technical trails.", "Footwear", 128.00m),
        [2] = new ProductRecord(2, "Wireless Earbuds", "Sweat-resistant earbuds with 30-hour battery life.", "Electronics", 89.00m),
        [3] = new ProductRecord(3, "Insulated Water Bottle", "Keeps drinks cold for 24 hours, hot for 12.", "Accessories", 32.00m),
        [4] = new ProductRecord(4, "Ultralight Rain Jacket", "Packable shell jacket, fully seam-sealed.", "Apparel", 210.00m),
        [5] = new ProductRecord(5, "Merino Wool Socks (3-Pack)", "Odor-resistant socks for multi-day trips.", "Apparel", 24.00m),
        [6] = new ProductRecord(6, "Foldable Trekking Poles", "Carbon-fiber poles that collapse to 15 inches.", "Outdoor Gear", 64.00m),
        [7] = new ProductRecord(7, "Limited Edition Hoodie", "Small-batch run; the warehouse feed for this one is flaky.", "Apparel", 75.00m),
        [8] = new ProductRecord(8, "Carbon Fiber Bike Helmet", "MIPS-equipped helmet with 14 vents.", "Accessories", 145.00m),
    };

    public async Task<ProductRecord> GetProductAsync(int sku, CancellationToken ct)
    {
        await SimulatedLatency.Delay(ct);

        return Products.TryGetValue(sku, out var product)
            ? product
            : throw new KeyNotFoundException($"No product with SKU {sku}.");
    }

    public async Task<IReadOnlyList<ProductRecord>> GetByCategoryAsync(string category, int excludingSku, CancellationToken ct)
    {
        await SimulatedLatency.Delay(ct);

        return Products.Values.Where(p => p.Category == category && p.Sku != excludingSku).ToList();
    }
}
