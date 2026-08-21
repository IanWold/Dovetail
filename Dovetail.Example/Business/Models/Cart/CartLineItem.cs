namespace Dovetail.Example.Business;

public record CartLineItem(
    Sku Sku,
    string Name,
    int Quantity,
    decimal UnitPrice
);
