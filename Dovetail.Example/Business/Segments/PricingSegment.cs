namespace Dovetail.Example.Business;

// Discounting is a business rule, not a data-access concern, so unlike its
// neighbors here it has no Infrastructure dependency at all — it's pure
// computation over a domain type Dovetail already has in hand.
internal class PricingSegment : IPipelineSegment<ProductInfo, PricingInfo>
{
    public Task<PricingInfo> ExecuteAsync(ProductInfo product, CancellationToken ct)
    {
        var discount = product.Category switch
        {
            "Apparel" => 0.15m,
            "Footwear" => 0.10m,
            _ => 0m
        };

        var currentPrice = Math.Round(product.BasePrice * (1 - discount), 2);

        return Task.FromResult(new PricingInfo(product.BasePrice, currentPrice, discount * 100));
    }
}
