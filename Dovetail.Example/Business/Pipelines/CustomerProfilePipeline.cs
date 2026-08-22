using Dovetail;

namespace Dovetail.Example.Business;

internal partial class CustomerProfilePipeline(
    [Segment] CustomerAccountSegment account,
    [Segment] LoyaltyStatusSegment loyalty
) : IPipeline<UserId, CustomerProfile>
{
    [Segment]
    private static CustomerProfile Assemble(CustomerAccount account, LoyaltyStatus loyalty) =>
        new(account, loyalty);
}
