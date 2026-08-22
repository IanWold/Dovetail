using Microsoft.CodeAnalysis;

namespace Dovetail;

internal readonly record struct SegmentParameterInfo(
    TypeDeclarationModel ContainingType,
    string? PipelineInputTypeNamesJoined,
    string? PipelineResultTypeName,
    string ParameterName,
    string ParameterTypeName,
    string? ValueAccessor,
    bool BackingMemberAmbiguous,
    string? SegmentTypeName,
    string? SegmentInputTypeNamesJoined,
    string? SegmentResultTypeName,
    Location? ParameterLocation,
    Location? ContainingTypeLocation,
    bool IsStaticSegmentMethod,
    StaticSegmentMethodProblem StaticSegmentMethodProblem,
    bool SegmentIsAsync,
    bool SegmentAcceptsCancellationToken
);
