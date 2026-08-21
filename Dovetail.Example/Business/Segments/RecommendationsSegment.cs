using Dovetail.Example.Infrastructure;

namespace Dovetail.Example.Business;

internal class RecommendationsSegment(ProductCatalogDataAccess catalog) : IPipelineSegment<ProductInfo, IReadOnlyList<RecommendedProduct>>
{
    public async Task<IReadOnlyList<RecommendedProduct>> ExecuteAsync(ProductInfo product, CancellationToken ct)
    {
        var records = await catalog.GetByCategoryAsync(product.Category, product.Sku.Value, ct);
        return records.Select(r => new RecommendedProduct(new Sku(r.Sku), r.Name)).ToList();
    }
}
