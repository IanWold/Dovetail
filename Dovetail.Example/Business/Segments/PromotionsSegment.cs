namespace Dovetail.Example.Business;

internal class PromotionsSegment : IPipelineSegment<UserId, IReadOnlyList<CartLineItem>, AppliedPromotions>
{
    public Task<AppliedPromotions> ExecuteAsync(UserId userId, IReadOnlyList<CartLineItem> items, CancellationToken ct)
    {
        if (items.Count == 0)
        {
            return Task.FromResult(new AppliedPromotions([], 0m));
        }

        var subtotal = items.Sum(i => i.UnitPrice * i.Quantity);
        var codes = new List<string>();
        var savings = 0m;

        if (subtotal > 150m)
        {
            codes.Add("FREESHIP150");
        }

        if (userId.Value == 1)
        {
            codes.Add("GOLD10");
            savings += subtotal * 0.10m;
        }

        return Task.FromResult(new AppliedPromotions(codes, Math.Round(savings, 2)));
    }
}
