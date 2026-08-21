namespace Dovetail.Example.Business;

// Promotion eligibility is a business rule computed entirely from data already
// in hand (no Infrastructure dependency needed), same as PricingSegment and
// ShippingEstimateSegment below. It still mixes a pipeline input (UserId) with
// another segment's result (CartLineItems) in one parameter list — the two
// dependency kinds DependencyBinding exists to tell apart.
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
