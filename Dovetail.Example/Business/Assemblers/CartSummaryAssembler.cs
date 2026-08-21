namespace Dovetail.Example.Business;

internal class CartSummaryAssembler : IPipelineSegment<IReadOnlyList<CartLineItem>, CartPricing, AppliedPromotions, ShippingEstimate, CustomerAccount, LoyaltyStatus, CartSummary>
{
    public Task<CartSummary> ExecuteAsync(
        IReadOnlyList<CartLineItem> items,
        CartPricing pricing,
        AppliedPromotions promotions,
        ShippingEstimate shipping,
        CustomerAccount account,
        LoyaltyStatus loyalty,
        CancellationToken ct
    ) =>
        Task.FromResult(new CartSummary(items, pricing, promotions, shipping, new CustomerProfile(account, loyalty)));
}
