; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category         | Severity | Notes
--------|------------------|----------|------------------------------------------------------------
MG0001  | Mirrorgen.Subset | Error    | LINQ is not allowed in [Transpile] methods. SubsetAnalyzer.
MG0002  | Mirrorgen.Subset | Error    | async / await / Task are not allowed in [Transpile] methods. SubsetAnalyzer.
