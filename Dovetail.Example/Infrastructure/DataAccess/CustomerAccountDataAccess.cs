namespace Dovetail.Example.Infrastructure;

internal class CustomerAccountDataAccess
{
    private static readonly Dictionary<int, CustomerAccountRecord> Accounts = new()
    {
        [1] = new CustomerAccountRecord("Alice Nguyen", "alice@example.com", "Gold"),
        [2] = new CustomerAccountRecord("Marcus Webb", "marcus@example.com", "Silver"),
        [3] = new CustomerAccountRecord("Priya Shah", "priya@example.com", "Gold"),
    };

    public async Task<CustomerAccountRecord> GetAccountAsync(int userId, CancellationToken ct)
    {
        await SimulatedLatency.Delay(ct);

        return Accounts.TryGetValue(userId, out var account)
            ? account
            : throw new KeyNotFoundException($"No customer with user ID {userId}.");
    }
}
