namespace Dovetail;

internal readonly record struct DependencyBinding(
    string? SegmentParameterName,
    int? PipelineInputIndex
);
