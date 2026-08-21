namespace Dovetail.Example.Business;

public record CartPricing(
    decimal Subtotal,
    decimal Discount,
    decimal Total
);
