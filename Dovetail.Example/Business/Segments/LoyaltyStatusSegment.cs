using Dovetail.Example.Infrastructure;

namespace Dovetail.Example.Business;

internal class LoyaltyStatusSegment(LoyaltyDataAccess loyalty) : IPipelineSegment<UserId, LoyaltyStatus>
{
    public async Task<LoyaltyStatus> ExecuteAsync(UserId userId, CancellationToken ct)
    {
        var record = await loyalty.GetStatusAsync(userId.Value, ct);
        // No loyalty record is a real, ordinary case (not every customer has enrolled),
        // and deciding what that means — "None" tier, zero points — is a business
        // rule, not something Infrastructure should be deciding.
        return record is null
            ? new LoyaltyStatus(0, 1000, "None")
            : new LoyaltyStatus(record.PointsBalance, record.PointsToNextTier, record.CurrentTier);
    }
}
