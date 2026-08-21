using Dovetail.Example.Infrastructure;

namespace Dovetail.Example.Business;

// The gateway returns a raw status code the way a real one actually would;
// mapping it into our own PaymentState enum is Business's job, not Infrastructure's.
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
