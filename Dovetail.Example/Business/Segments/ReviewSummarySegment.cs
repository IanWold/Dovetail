using Dovetail.Example.Infrastructure;

namespace Dovetail.Example.Business;

internal class ReviewSummarySegment(ReviewDataAccess reviews) : IPipelineSegment<Sku, ReviewSummary>
{
    public async Task<ReviewSummary> ExecuteAsync(Sku sku, CancellationToken ct)
    {
        var record = await reviews.GetSummaryAsync(sku.Value, ct);
        return record is null ? new ReviewSummary(0, 0) : new ReviewSummary(record.AverageRating, record.ReviewCount);
    }
}
