namespace Dovetail.Example.Business;

internal class CartPricingSegment : IPipelineSegment<IReadOnlyList<CartLineItem>, CartPricing>
{
    public Task<CartPricing> ExecuteAsync(IReadOnlyList<CartLineItem> items, CancellationToken ct)
    {
        var subtotal = items.Sum(i => i.UnitPrice * i.Quantity);
        return Task.FromResult(new CartPricing(subtotal, 0m, subtotal));
    }
}
