; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DOVE021 | Dovetail.SourceGenerator | Error | Segment dependency ambiguity depends on another unresolved ambiguity
DOVE022 | Dovetail.SourceGenerator | Error | Segment or pipeline isn't accessible for automatic DI registration
DOVE023 | Dovetail.SourceGenerator | Error | MaxConcurrency's argument doesn't match what it's applied to
DOVE024 | Dovetail.SourceGenerator | Error | MaxConcurrency property must be a readable, non-static int
DOVE025 | Dovetail.SourceGenerator | Error | A pipeline can declare at most one MaxConcurrency source
