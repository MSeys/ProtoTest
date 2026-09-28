; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PT0001  | ProtoTest | Warning  | ProtoTest test attribute combined with the runner's own test attribute
PT0002  | ProtoTest | Warning  | ProtoTest context used in a test without the ProtoTest attribute
