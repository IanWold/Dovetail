namespace Dovetail;

/// <summary>
/// Marks a pipeline's constructor parameter or a static method declared in the pipeline's bodyd
/// as a segment to wire into its generated <c>ExecuteAsync</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Method)]
public sealed class SegmentAttribute : Attribute { }