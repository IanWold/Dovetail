; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DOVE001 | Dovetail.SourceGenerator | Error | Containing type must be partial
DOVE002 | Dovetail.SourceGenerator | Error | Containing type must implement IPipeline
DOVE003 | Dovetail.SourceGenerator | Error | Segment type must implement IPipelineSegment
DOVE004 | Dovetail.SourceGenerator | Error | No segment produces the pipeline result
DOVE005 | Dovetail.SourceGenerator | Error | More than one segment produces the same type
DOVE006 | Dovetail.SourceGenerator | Error | Segment dependency could not be resolved
DOVE007 | Dovetail.SourceGenerator | Error | Segment dependency cycle detected
DOVE008 | Dovetail.SourceGenerator | Error | Segment is unreachable from the pipeline result
DOVE009 | Dovetail.SourceGenerator | Error | The pipeline declares the same input type more than once
DOVE010 | Dovetail.SourceGenerator | Error | Segment parameter's backing field or property could not be determined
DOVE011 | Dovetail.SourceGenerator | Error | Segment parameter's backing field or property is ambiguous
DOVE012 | Dovetail.SourceGenerator | Error | A [Segment] method must be static
DOVE013 | Dovetail.SourceGenerator | Error | A [Segment] method must return a value
