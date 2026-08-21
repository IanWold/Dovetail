namespace Dovetail.Example.Business;

internal class CustomerProfileAssembler : IPipelineSegment<CustomerAccount, LoyaltyStatus, CustomerProfile>
{
    public Task<CustomerProfile> ExecuteAsync(CustomerAccount account, LoyaltyStatus loyalty, CancellationToken ct) =>
        Task.FromResult(new CustomerProfile(account, loyalty));
}
