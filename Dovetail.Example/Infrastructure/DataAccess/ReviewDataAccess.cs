namespace Dovetail.Example.Infrastructure;

internal class ReviewDataAccess
{
    private static readonly Dictionary<int, ReviewRecord> Reviews = new()
    {
        [1] = new ReviewRecord(4.6, 812),
        [2] = new ReviewRecord(4.1, 340),
        [3] = new ReviewRecord(4.8, 96),
        [4] = new ReviewRecord(3.9, 51),
        [5] = new ReviewRecord(4.4, 205),
        [6] = new ReviewRecord(4.2, 33),
        [7] = new ReviewRecord(4.7, 18),
        [8] = new ReviewRecord(4.0, 64),
    };

    public async Task<ReviewRecord?> GetSummaryAsync(int sku, CancellationToken ct)
    {
        await SimulatedLatency.Delay(ct);

        return Reviews.TryGetValue(sku, out var review) ? review : null;
    }
}
