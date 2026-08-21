namespace Dovetail.Example.Business;

public record ProductInfo(
    Sku Sku,
    string Name,
    string Description,
    string Category,
    decimal BasePrice
);
