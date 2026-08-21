namespace Dovetail.Example.Business;

public record OrderDetails(
    OrderId OrderId,
    UserId UserId,
    IReadOnlyList<CartLineItem> Items,
    decimal Total,
    DateTimeOffset PlacedAt
);
