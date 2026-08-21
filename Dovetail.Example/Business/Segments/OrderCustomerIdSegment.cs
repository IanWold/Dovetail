namespace Dovetail.Example.Business;

// This pipeline's own input is an OrderId, but CustomerProfilePipeline needs a
// UserId. This segment's whole job is projecting one out of OrderDetails so it
// exists as a distinct, type-matchable node in the graph.
internal class OrderCustomerIdSegment : IPipelineSegment<OrderDetails, UserId>
{
    public Task<UserId> ExecuteAsync(OrderDetails order, CancellationToken ct) =>
        Task.FromResult(order.UserId);
}
