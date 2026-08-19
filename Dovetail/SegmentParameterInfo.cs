using Microsoft.CodeAnalysis;

namespace Dovetail;

internal readonly record struct SegmentParameterInfo(
    TypeDeclarationModel ContainingType,
    string? PipelineInputTypeName,
    string? PipelineResultTypeName,
    string ParameterName,
    string? SegmentInputTypeNamesJoined,
    string? SegmentResultTypeName,
    Location? ParameterLocation,
    Location? ContainingTypeLocation
);
