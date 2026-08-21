namespace Dovetail.Example.Business;

internal class ShippingEstimateSegment : IPipelineSegment<UserId, IReadOnlyList<CartLineItem>, ShippingEstimate>
{
    public Task<ShippingEstimate> ExecuteAsync(UserId userId, IReadOnlyList<CartLineItem> items, CancellationToken ct)
    {
        if (items.Count == 0)
        {
            return Task.FromResult(new ShippingEstimate("N/A", 0m, 0));
        }

        var totalUnits = items.Sum(i => i.Quantity);

        var estimate = totalUnits switch
        {
            <= 2 => new ShippingEstimate("Standard", 5.99m, 5),
            <= 5 => new ShippingEstimate("Standard", 8.99m, 5),
            _ => new ShippingEstimate("Freight", 19.99m, 8)
        };

        return Task.FromResult(estimate);
    }
}
