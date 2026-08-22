namespace Dovetail;

/// <summary>
/// Bounds how many of this pipeline's segments may execute concurrently at once.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class MaxConcurrencyAttribute(int maxConcurrency) : Attribute
{
    /// <summary>
    /// The maximum number of segments allowed to execute concurrently.
    /// </summary>
    public int MaxConcurrency { get; } = maxConcurrency;
}
