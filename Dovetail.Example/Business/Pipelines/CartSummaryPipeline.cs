using Dovetail;

namespace Dovetail.Example.Business;

// CustomerAccountSegment and LoyaltyStatusSegment are the exact same classes
// CustomerProfilePipeline is built from — reused here directly rather than through
// that pipeline, since the cart page wants the two pieces separately rather than
// pre-bundled. OrderConfirmationPipeline instead reuses CustomerProfilePipeline as
// a whole: two different ways of sharing the same underlying work.
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
