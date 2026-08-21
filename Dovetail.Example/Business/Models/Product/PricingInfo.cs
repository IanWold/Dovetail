namespace Dovetail.Example.Business;

public record PricingInfo(
    decimal OriginalPrice,
    decimal CurrentPrice,
    decimal DiscountPercent
);
