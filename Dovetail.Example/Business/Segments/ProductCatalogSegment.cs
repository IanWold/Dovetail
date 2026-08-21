using Dovetail.Example.Infrastructure;

namespace Dovetail.Example.Business;

internal class ProductCatalogSegment(ProductCatalogDataAccess catalog) : IPipelineSegment<Sku, ProductInfo>
{
    public async Task<ProductInfo> ExecuteAsync(Sku sku, CancellationToken ct)
    {
        var record = await catalog.GetProductAsync(sku.Value, ct);
        return new ProductInfo(new Sku(record.Sku), record.Name, record.Description, record.Category, record.BasePrice);
    }
}
