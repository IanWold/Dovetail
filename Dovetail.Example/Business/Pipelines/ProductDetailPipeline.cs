using Dovetail;

namespace Dovetail.Example.Business;

/// <summary>
/// Demonstrates diamond-shaped pipeline: pricing and recommendations depend on catalog.
/// Also demonstrates <see cref="MaxConcurrencyAttribute"/>: at most 2 segments run at once, so this page's
/// lookups don't all hit the backing services in the same instant.
/// </summary>
[MaxConcurrency(2)]
internal partial class ProductDetailPipeline(
    [Segment] ProductCatalogSegment catalog,
    [Segment] PricingSegment pricing,
    [Segment] InventorySegment inventory,
    [Segment] ReviewSummarySegment reviews,
    [Segment] RecommendationsSegment recommendations
) : IPipeline<Sku, ProductDetail>
{
    [Segment]
    private static ProductDetail Assemble(
        ProductInfo info,
        PricingInfo pricing,
        InventoryStatus inventory,
        ReviewSummary reviews,
        IReadOnlyList<RecommendedProduct> recommendations
    ) =>
        new(info, pricing, inventory, reviews, recommendations);
}
