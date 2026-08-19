using System.Collections.Immutable;

namespace Dovetail;

internal readonly record struct SegmentModel(
    string ParameterName,
    ImmutableArray<string> InputTypeNames,
    string ResultTypeName
);
