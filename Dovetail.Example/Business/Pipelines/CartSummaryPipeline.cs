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
    [Segment] LoyaltyStatusSegment loyalty,
    [Segment] CartSummaryAssembler assembler
) : IPipeline<UserId, CartId, CartSummary>;
