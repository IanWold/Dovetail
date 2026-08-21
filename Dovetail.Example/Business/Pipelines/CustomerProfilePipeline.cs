using Dovetail;

namespace Dovetail.Example.Business;

// Small on its own, and reused as a [Segment] inside OrderConfirmationPipeline —
// implementing both IPipeline and the matching IPipelineSegment shape is what makes
// that reuse work with no extra code. CartSummaryPipeline reuses this pipeline's two
// segments directly instead of the whole thing; see the comment there.
internal partial class CustomerProfilePipeline(
    [Segment] CustomerAccountSegment account,
    [Segment] LoyaltyStatusSegment loyalty
) : IPipeline<UserId, CustomerProfile>, IPipelineSegment<UserId, CustomerProfile>
{
    [Segment]
    private static CustomerProfile Assemble(CustomerAccount account, LoyaltyStatus loyalty) =>
        new(account, loyalty);
}
