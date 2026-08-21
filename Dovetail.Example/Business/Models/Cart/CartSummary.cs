namespace Dovetail.Example.Business;

public record CartSummary(
    IReadOnlyList<CartLineItem> Items,
    CartPricing Pricing,
    AppliedPromotions Promotions,
    ShippingEstimate Shipping,
    CustomerProfile Customer
);
