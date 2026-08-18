using System.Collections.Immutable;

namespace Dovetail;

/// <summary>
/// A namespace/name pair identifying the partial type that declared a
/// <c>[Segment]</c>-annotated parameter. Equatable so the incremental
/// pipeline can skip re-emitting output when nothing relevant changed.
/// </summary>
internal readonly record struct TypeDeclarationModel(string Namespace, string Name, bool IsPartial);

/// <summary>
/// Everything discovered about a single <c>[Segment]</c>-annotated parameter, extracted
/// while the containing type's symbols are still available. <see cref="PipelineResultTypeName"/>
/// is null when the containing type doesn't implement <c>IPipeline&lt;...&gt;</c>; <see cref="SegmentResultTypeName"/>
/// is null when the parameter's type doesn't implement exactly one <c>IPipelineSegment&lt;...&gt;</c>.
/// Both are captured redundantly per-parameter (rather than once per pipeline) because only
/// primitive, equatable data survives past <c>Collect()</c> in an incremental generator.
/// </summary>
internal readonly record struct SegmentParameterInfo(
    TypeDeclarationModel ContainingType,
    string? PipelineInputTypeName,
    string? PipelineResultTypeName,
    string ParameterName,
    string? SegmentInputTypeNamesJoined,
    string? SegmentResultTypeName);

/// <summary>
/// A validated segment, ready for dependency resolution and codegen.
/// </summary>
internal readonly record struct SegmentModel(string ParameterName, ImmutableArray<string> InputTypeNames, string ResultTypeName);
