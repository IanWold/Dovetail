using System.Collections.Generic;
using System.Collections.Immutable;

namespace Dovetail;

internal readonly record struct PipelineGraphModel(
    TypeDeclarationModel ContainingType,
    ImmutableArray<string> PipelineInputTypeNames,
    string PipelineResultTypeName,
    ImmutableArray<SegmentModel> Segments,
    Dictionary<string, ImmutableArray<DependencyBinding>> Dependencies,
    string TerminalParameterName,
    int? MaxConcurrency
);
