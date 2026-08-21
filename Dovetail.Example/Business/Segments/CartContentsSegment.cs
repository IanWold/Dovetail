using Dovetail.Example.Infrastructure;

namespace Dovetail.Example.Business;

// Needs both of the pipeline's own inputs directly — a real use of the
// multi-arity IPipeline<T1, T2, TResult> matching, not just a single input.
internal class CartContentsSegment(CartDataAccess carts) : IPipelineSegment<UserId, CartId, IReadOnlyList<CartLineItem>>
{
    public async Task<IReadOnlyList<CartLineItem>> ExecuteAsync(UserId userId, CartId cartId, CancellationToken ct)
    {
        var record = await carts.GetCartAsync(userId.Value, cartId.Value, ct);
        return record.Items.Select(i => new CartLineItem(new Sku(i.Sku), i.Name, i.Quantity, i.UnitPrice)).ToList();
    }
}
