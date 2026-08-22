using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Dovetail;

internal readonly record struct SegmentModel(
    string ParameterName,
    string ValueAccessor,
    string SegmentTypeName,
    ImmutableArray<string> InputTypeNames,
    string ResultTypeName,
    Location? ParameterLocation,
    bool IsStaticMethod,
    bool IsAsync,
    bool AcceptsCancellationToken
);
