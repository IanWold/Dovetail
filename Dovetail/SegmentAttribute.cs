namespace Dovetail;

/// <summary>
/// Marks a pipeline's constructor parameter as a segment to wire into its generated <c>ExecuteAsync</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class SegmentAttribute : Attribute { }