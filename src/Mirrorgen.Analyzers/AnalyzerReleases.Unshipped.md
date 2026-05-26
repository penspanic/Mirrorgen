; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category         | Severity | Notes
--------|------------------|----------|------------------------------------------------------------
MG0001  | Mirrorgen.Subset | Error    | LINQ is not allowed in [Transpile] methods. SubsetAnalyzer.
MG0002  | Mirrorgen.Subset | Error    | async / await / Task are not allowed in [Transpile] methods. SubsetAnalyzer.
MG0003  | Mirrorgen.Subset | Error    | Span / ref / in / out / unsafe / pointer are not allowed in [Transpile] methods. SubsetAnalyzer.
MG0004  | Mirrorgen.Subset | Error    | throw is not allowed in [Transpile] methods. SubsetAnalyzer.
MG0005  | Mirrorgen.Subset | Error    | Reflection is not allowed in [Transpile] methods. SubsetAnalyzer.
MG0006  | Mirrorgen.Subset | Error    | Inheritance is not allowed on the declaring type of a [Transpile] method. SubsetAnalyzer.
