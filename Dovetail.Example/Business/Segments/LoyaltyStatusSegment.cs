using Dovetail.Example.Infrastructure;

namespace Dovetail.Example.Business;

internal class LoyaltyStatusSegment(LoyaltyDataAccess loyalty) : IPipelineSegment<UserId, LoyaltyStatus>
{
    public async Task<LoyaltyStatus> ExecuteAsync(UserId userId, CancellationToken ct)
    {
        var record = await loyalty.GetStatusAsync(userId.Value, ct);
        return record is null
            ? new LoyaltyStatus(0, 1000, "None")
            : new LoyaltyStatus(record.PointsBalance, record.PointsToNextTier, record.CurrentTier);
    }
}
