using Microsoft.CodeAnalysis;

namespace Dovetail;

internal readonly record struct SegmentParameterInfo(
    TypeDeclarationModel ContainingType,
    string? PipelineInputTypeNamesJoined,
    string? PipelineResultTypeName,
    string ParameterName,
    string? SegmentTypeName,
    string? SegmentInputTypeNamesJoined,
    string? SegmentResultTypeName,
    Location? ParameterLocation,
    Location? ContainingTypeLocation
);
