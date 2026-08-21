namespace Dovetail.Example.Infrastructure;

internal record LoyaltyRecord(
    int PointsBalance,
    int PointsToNextTier,
    string CurrentTier
);
