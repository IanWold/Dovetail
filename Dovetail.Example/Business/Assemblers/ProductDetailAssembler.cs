namespace Dovetail.Example.Business;

internal class ProductDetailAssembler : IPipelineSegment<ProductInfo, PricingInfo, InventoryStatus, ReviewSummary, IReadOnlyList<RecommendedProduct>, ProductDetail>
{
    public Task<ProductDetail> ExecuteAsync(
        ProductInfo info,
        PricingInfo pricing,
        InventoryStatus inventory,
        ReviewSummary reviews,
        IReadOnlyList<RecommendedProduct> recommendations,
        CancellationToken ct
    ) =>
        Task.FromResult(new ProductDetail(info, pricing, inventory, reviews, recommendations));
}
