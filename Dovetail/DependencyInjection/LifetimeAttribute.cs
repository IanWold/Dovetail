namespace Dovetail.DependencyInjection;

/// <summary>
/// Sets the lifetime <c>AddPipelines()</c> registers this pipeline or segment with, instead of the default of <see cref="ServiceLifetime.Transient"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class LifetimeAttribute(ServiceLifetime lifetime) : Attribute
{
    /// <summary>The lifetime to register this pipeline or segment with.</summary>
    public ServiceLifetime Lifetime { get; } = lifetime;
}
