namespace Dovetail.Example.Business;

public record ProductDetail(
    ProductInfo Info,
    PricingInfo Pricing,
    InventoryStatus Inventory,
    ReviewSummary Reviews,
    IReadOnlyList<RecommendedProduct> Recommendations
);
