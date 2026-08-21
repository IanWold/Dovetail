using Dovetail;

namespace Dovetail.Example.Business;

/// <summary>
/// Demonstrates reusing <see cref="CustomerAccountSegment"/> and <see cref="LoyaltyStatusSegment"/>
/// </summary>
internal partial class CartSummaryPipeline(
    [Segment] CartContentsSegment cart,
    [Segment] CartPricingSegment pricing,
    [Segment] PromotionsSegment promotions,
    [Segment] ShippingEstimateSegment shipping,
    [Segment] CustomerAccountSegment account,
    [Segment] LoyaltyStatusSegment loyalty
) : IPipeline<UserId, CartId, CartSummary>
{
    [Segment]
    private static CartSummary Assemble(
        IReadOnlyList<CartLineItem> items,
        CartPricing pricing,
        AppliedPromotions promotions,
        ShippingEstimate shipping,
        CustomerAccount account,
        LoyaltyStatus loyalty
    ) =>
        new(items, pricing, promotions, shipping, new CustomerProfile(account, loyalty));
}
