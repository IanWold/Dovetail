using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Dovetail;

internal readonly record struct MaxConcurrencySourceResolution(
    string? PropertyName,
    Location? PropertyLocation,
    MaxConcurrencySourceProblem MaxConcurrencySourceProblem,
    string? InvalidPropertyName,
    ImmutableArray<string> ConflictingSourceDescriptions
);
