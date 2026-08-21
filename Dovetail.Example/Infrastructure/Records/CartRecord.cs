namespace Dovetail.Example.Infrastructure;

internal record CartRecord(
    IReadOnlyList<CartItemRecord> Items
);
