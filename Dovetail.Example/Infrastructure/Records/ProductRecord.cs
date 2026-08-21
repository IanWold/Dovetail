namespace Dovetail.Example.Infrastructure;

internal record ProductRecord(
    int Sku,
    string Name,
    string Description,
    string Category,
    decimal BasePrice
);
