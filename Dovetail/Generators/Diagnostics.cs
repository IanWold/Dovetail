using Microsoft.CodeAnalysis;

namespace Dovetail;

internal static class Diagnostics
{
    internal static readonly DiagnosticDescriptor ContainingTypeMustBePartial = new(
        id: "DOVE001",
        title: "Containing type must be partial",
        messageFormat: "'{0}' declares a [Segment] parameter but is not partial; add the `partial` modifier so Dovetail can generate into it",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor ContainingTypeMustImplementPipeline = new(
        id: "DOVE002",
        title: "Containing type must implement IPipeline",
        messageFormat: "'{0}' declares a [Segment] parameter but does not implement exactly one IPipeline<...> interface; implement IPipeline<TResult> or one of its multi-input variants exactly once",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor SegmentTypeMustImplementPipelineSegment = new(
        id: "DOVE003",
        title: "Segment type must implement IPipelineSegment",
        messageFormat: "The type of [Segment] parameter '{0}' must implement exactly one IPipelineSegment<...> interface; if its concrete type implements more than one, type the parameter as the specific interface you mean instead",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor MissingTerminalSegment = new(
        id: "DOVE004",
        title: "No segment produces the pipeline result",
        messageFormat: "'{0}' implements IPipeline<..., {1}> but no segment produces '{1}'; add a segment whose result type is '{1}', or change the pipeline's declared result type",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor DuplicateSegmentResult = new(
        id: "DOVE005",
        title: "More than one segment produces the same type",
        messageFormat: "Segments {0} all produce '{1}'; each type may be produced by only one segment in a pipeline; change one of their result types, or remove the extras",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor UnresolvedDependency = new(
        id: "DOVE006",
        title: "Segment dependency could not be resolved",
        messageFormat: "Parameter '{0}' needs an input of type '{1}', but no segment produces it and it isn't one of the pipeline's own input types; add a segment that produces it, or declare it as one of the pipeline's inputs",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor DependencyCycle = new(
        id: "DOVE007",
        title: "Segment dependency cycle detected",
        messageFormat: "'{0}' has a segment dependency cycle: {1}; break the cycle by removing or redirecting one of these dependencies",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor UnreachableSegment = new(
        id: "DOVE008",
        title: "Segment is unreachable from the pipeline result",
        messageFormat: "Parameter '{0}' is never used, directly or transitively, by the segment that produces '{1}'; its failures could go unobserved, so every segment must feed the pipeline result; remove this segment, or have another segment consume its result on the way to '{1}'",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor DuplicatePipelineInput = new(
        id: "DOVE009",
        title: "The pipeline declares the same input type more than once",
        messageFormat: "'{0}' declares more than one input of type {1}; each of a pipeline's own input types must be unique; wrap one of the duplicates in its own type, or combine them into a single input",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor SegmentBackingMemberNotFound = new(
        id: "DOVE010",
        title: "Segment parameter's backing field or property could not be determined",
        messageFormat: "'{0}' declares [Segment] parameter '{1}' on a constructor that is not a primary constructor, but has no field or property of type {2} to read its value from; use a primary constructor, or add exactly one field or property of that type",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor AmbiguousSegmentBackingMember = new(
        id: "DOVE011",
        title: "Segment parameter's backing field or property is ambiguous",
        messageFormat: "'{0}' declares [Segment] parameter '{1}' on a constructor that is not a primary constructor, and has more than one field or property of type {2}; Dovetail can't tell which one holds the segment's value, so use a primary constructor, or ensure only one field or property has that type",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor SegmentMethodMustBeStatic = new(
        id: "DOVE012",
        title: "A [Segment] method must be static",
        messageFormat: "'{0}' declares [Segment] on method '{1}', but the method isn't static; add the `static` modifier to '{1}'",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor SegmentMethodMustReturnAValue = new(
        id: "DOVE013",
        title: "A [Segment] method must return a value",
        messageFormat: "'{0}' declares [Segment] on method '{1}', but it doesn't return a value; change '{1}''s return type to a result type or Task<TResult>",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor ContainingAncestorMustBePartial = new(
        id: "DOVE014",
        title: "An ancestor of a nested pipeline type must be partial",
        messageFormat: "'{0}' is nested inside '{1}', which isn't partial; add the `partial` modifier to '{1}' so Dovetail can generate into it",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor NestedInGenericType = new(
        id: "DOVE015",
        title: "A pipeline can't be nested inside a generic type",
        messageFormat: "'{0}' is nested inside generic type '{1}'; Dovetail doesn't support generating into a pipeline nested inside a generic type; move '{0}' out of '{1}', or make '{1}' non-generic if it doesn't need to be",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor SegmentMethodCannotHaveOwnTypeParameters = new(
        id: "DOVE016",
        title: "A [Segment] method can't have its own type parameters",
        messageFormat: "'{0}' declares [Segment] on method '{1}', which has its own type parameters; a segment method can use the pipeline's type parameters, but can't introduce new ones of its own; remove '{1}''s type parameters",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor DuplicateSegmentInterfaceImplementation = new(
        id: "DOVE017",
        title: "More than one segment implements the same IPipelineSegment<...> interface",
        messageFormat: "Segments {0} all implement '{1}'; AddPipelines() registers every segment against every IPipelineSegment<...> interface it implements, so it can't tell which of these to use for '{1}'; give each segment a distinct input or result type so their shapes no longer match",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor AmbiguousDependency = new(
        id: "DOVE018",
        title: "Segment dependency ambiguously matches a pipeline input and a segment's result",
        messageFormat: "Parameter '{0}' needs an input of type '{1}', but it matches both a pipeline input and segment '{2}', which also produces this type; give one of them a distinct type so Dovetail can tell which you mean",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor InvalidMaxConcurrency = new(
        id: "DOVE019",
        title: "MaxConcurrency must be a positive integer",
        messageFormat: "'{0}' declares [MaxConcurrency({1})], but the value must be 1 or greater; use a positive integer, or remove the attribute to leave concurrency unbounded",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    internal static readonly DiagnosticDescriptor AmbiguousResultChain = new(
        id: "DOVE020",
        title: "Segments producing the same type don't form a single valid chain",
        messageFormat: "Segments {0} all produce '{1}', but Dovetail can't tell what order they'd run in; at most one segment may both consume and produce '{1}' in the same pipeline; remove the extras, or restructure so only one segment transforms '{1}' into itself",
        category: "Dovetail.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
}