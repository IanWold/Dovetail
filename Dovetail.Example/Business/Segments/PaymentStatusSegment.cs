using Dovetail.Example.Infrastructure;

namespace Dovetail.Example.Business;

internal class PaymentStatusSegment(PaymentDataAccess payments) : IPipelineSegment<OrderDetails, PaymentStatus>
{
    public async Task<PaymentStatus> ExecuteAsync(OrderDetails order, CancellationToken ct)
    {
        var record = await payments.GetStatusAsync(order.OrderId.Value, ct);
        var state = record.StatusCode switch
        {
            "PAID" => PaymentState.Paid,
            "FAILED" => PaymentState.Failed,
            _ => PaymentState.Pending
        };
        return new PaymentStatus(state, record.TransactionId);
    }
}
