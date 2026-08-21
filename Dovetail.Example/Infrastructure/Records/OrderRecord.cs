namespace Dovetail.Example.Infrastructure;

internal record OrderRecord(
    int UserId,
    IReadOnlyList<CartItemRecord> Items,
    decimal Total,
    DateTimeOffset PlacedAt
);
