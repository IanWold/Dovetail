namespace Dovetail.Example.Infrastructure;

internal record CartItemRecord(
    int Sku,
    string Name,
    int Quantity,
    decimal UnitPrice
);
