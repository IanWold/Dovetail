using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Dovetail;

internal readonly record struct SegmentModel(
    string ParameterName,
    string SegmentTypeName,
    ImmutableArray<string> InputTypeNames,
    string ResultTypeName,
    Location? ParameterLocation
);
