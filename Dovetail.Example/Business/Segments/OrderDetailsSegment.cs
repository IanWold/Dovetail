using Dovetail.Example.Infrastructure;

namespace Dovetail.Example.Business;

internal class OrderDetailsSegment(OrderDataAccess orders) : IPipelineSegment<OrderId, OrderDetails>
{
    public async Task<OrderDetails> ExecuteAsync(OrderId orderId, CancellationToken ct)
    {
        var record = await orders.GetOrderAsync(orderId.Value, ct);
        var items = record.Items.Select(i => new CartLineItem(new Sku(i.Sku), i.Name, i.Quantity, i.UnitPrice)).ToList();
        return new OrderDetails(orderId, new UserId(record.UserId), items, record.Total, record.PlacedAt);
    }
}
