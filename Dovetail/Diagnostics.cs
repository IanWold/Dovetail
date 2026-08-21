using Microsoft.CodeAnalysis;

namespace Dovetail;

internal static class Diagnostics
{
    internal static readonly DiagnosticDescriptor ContainingTypeMustBePartial = new(
        id: "DOVE001",
        title: "Containing type must be partial",
        messageFormat: "'{0}' declares a [Segment] parameter but is not partial",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor ContainingTypeMustImplementPipeline = new(
        id: "DOVE002",
        title: "Containing type must implement IPipeline",
        messageFormat: "'{0}' declares a [Segment] parameter but does not implement exactly one IPipeline<...> interface",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor SegmentTypeMustImplementPipelineSegment = new(
        id: "DOVE003",
        title: "Segment type must implement IPipelineSegment",
        messageFormat: "The type of [Segment] parameter '{0}' must implement exactly one IPipelineSegment<...> interface",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor MissingTerminalSegment = new(
        id: "DOVE004",
        title: "No segment produces the pipeline result",
        messageFormat: "'{0}' implements IPipeline<..., {1}> but no segment produces '{1}'",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor DuplicateSegmentResult = new(
        id: "DOVE005",
        title: "More than one segment produces the same type",
        messageFormat: "Segments {0} all produce '{1}'; each type may be produced by only one segment in a pipeline",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor UnresolvedDependency = new(
        id: "DOVE006",
        title: "Segment dependency could not be resolved",
        messageFormat: "Parameter '{0}' needs an input of type '{1}', but {2}",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor DependencyCycle = new(
        id: "DOVE007",
        title: "Segment dependency cycle detected",
        messageFormat: "'{0}' has a segment dependency cycle: {1}",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor UnreachableSegment = new(
        id: "DOVE008",
        title: "Segment is unreachable from the pipeline result",
        messageFormat: "Parameter '{0}' is never used, directly or transitively, by the segment that produces '{1}'; its failures could go unobserved, so every segment must feed the pipeline result",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor DuplicatePipelineInput = new(
        id: "DOVE009",
        title: "The pipeline declares the same input type more than once",
        messageFormat: "'{0}' declares more than one input of type {1}; each of a pipeline's own input types must be unique",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
}