using Dovetail;

namespace Dovetail.Example.Business;

// Pricing and recommendations both depend on the catalog lookup's result rather
// than the raw SKU, so this is a real diamond, not just a flat fan-out from the input.
internal partial class ProductDetailPipeline(
    [Segment] ProductCatalogSegment catalog,
    [Segment] PricingSegment pricing,
    [Segment] InventorySegment inventory,
    [Segment] ReviewSummarySegment reviews,
    [Segment] RecommendationsSegment recommendations,
    [Segment] ProductDetailAssembler assembler
) : IPipeline<Sku, ProductDetail>;
