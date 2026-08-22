namespace Dovetail.Example.Infrastructure;

/// <summary>
/// Stands in for a real feature-flag provider. Toggle "Features:EnableCustomerProfileLookup" in configuration
/// to see <see cref="Business.ConditionalCustomerProfileSegment"/> take the fallback branch instead.
/// </summary>
internal class FeatureFlags(IConfiguration configuration)
{
    public bool EnableCustomerProfileLookup => configuration.GetValue("Features:EnableCustomerProfileLookup", true);
}
