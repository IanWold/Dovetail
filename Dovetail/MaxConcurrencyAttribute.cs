namespace Dovetail;

/// <summary>
/// Bounds how many of this pipeline's segments may execute concurrently at once.
/// <para>
/// Apply <c>[MaxConcurrency(n)]</c> to the pipeline itself for a limit fixed at compile time, or apply the
/// parameterless <c>[MaxConcurrency]</c> to a non-static, readable <see cref="int"/> property of the pipeline
/// to supply the limit at runtime instead. A pipeline may declare one or the other, but not both.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property)]
public sealed class MaxConcurrencyAttribute(int maxConcurrency) : Attribute
{
    /// <summary>
    /// Declares that the property this attribute is applied to supplies the pipeline's concurrency limit.
    /// The property is read once per <c>ExecuteAsync</c> call, so the limit can be configured at runtime.
    /// </summary>
    public MaxConcurrencyAttribute()
        : this(0)
    {
    }

    /// <summary>
    /// The maximum number of segments allowed to execute concurrently. Only meaningful when this attribute
    /// is applied to a pipeline with an explicit value; the property form supplies its limit at runtime.
    /// </summary>
    public int MaxConcurrency { get; } = maxConcurrency;
}
