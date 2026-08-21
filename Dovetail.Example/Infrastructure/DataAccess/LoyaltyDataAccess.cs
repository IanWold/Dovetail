namespace Dovetail.Example.Infrastructure;

internal class LoyaltyDataAccess
{
    private static readonly Dictionary<int, LoyaltyRecord> Statuses = new()
    {
        [1] = new LoyaltyRecord(4200, 800, "Gold"),
        [2] = new LoyaltyRecord(650, 350, "Silver"),
        [3] = new LoyaltyRecord(9100, 900, "Gold"),
    };

    public async Task<LoyaltyRecord?> GetStatusAsync(int userId, CancellationToken ct)
    {
        await SimulatedLatency.Delay(ct);

        return Statuses.TryGetValue(userId, out var status) ? status : null;
    }
}
