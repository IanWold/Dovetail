namespace Dovetail.Example.Business;

public record LoyaltyStatus(
    int PointsBalance,
    int PointsToNextTier,
    string CurrentTier
);
