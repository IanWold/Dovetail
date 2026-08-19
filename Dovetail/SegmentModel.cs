using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Dovetail;

internal readonly record struct SegmentModel(
    string ParameterName,
    ImmutableArray<string> InputTypeNames,
    string ResultTypeName,
    Location? ParameterLocation
);
