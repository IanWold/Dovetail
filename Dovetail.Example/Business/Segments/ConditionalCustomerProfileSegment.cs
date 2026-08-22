using Dovetail.Example.Infrastructure;

namespace Dovetail.Example.Business;

/// <summary>
/// Demonstrates conditional segment execution: wraps <see cref="CustomerProfilePipeline"/> so its lookup
/// can be skipped behind a feature flag, falling back to a minimal, unresolved profile instead.
/// </summary>
internal class ConditionalCustomerProfileSegment(
    FeatureFlags flags,
    CustomerProfilePipeline profile
) : IPipelineSegment<UserId, CustomerProfile>
{
    public Task<CustomerProfile> ExecuteAsync(UserId userId, CancellationToken ct) =>
        flags.EnableCustomerProfileLookup
            ? profile.ExecuteAsync(userId, ct)
            : Task.FromResult(new CustomerProfile(
                new CustomerAccount(userId, "Unknown", "", "Standard"),
                new LoyaltyStatus(0, 0, "Standard")
            ));
}
