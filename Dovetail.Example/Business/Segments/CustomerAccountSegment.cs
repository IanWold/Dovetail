using Dovetail.Example.Infrastructure;

namespace Dovetail.Example.Business;

internal class CustomerAccountSegment(CustomerAccountDataAccess accounts) : IPipelineSegment<UserId, CustomerAccount>
{
    public async Task<CustomerAccount> ExecuteAsync(UserId userId, CancellationToken ct)
    {
        var record = await accounts.GetAccountAsync(userId.Value, ct);
        return new CustomerAccount(userId, record.FullName, record.Email, record.MembershipTier);
    }
}
