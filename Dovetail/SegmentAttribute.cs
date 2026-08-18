namespace Dovetail;

/// <summary>
/// Marks a pipeline's primary-constructor parameter as a segment to wire into
/// its generated <c>ExecuteAsync</c>. The segment's inputs/result are read from
/// whichever <see cref="IPipelineSegment{TResult}"/> variant the parameter's own
/// type implements.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class SegmentAttribute : Attribute
{
}
