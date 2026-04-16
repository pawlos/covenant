# dotnet-callgraph-closure

A .NET analog of Ferrocene's callgraph-closure lint. Milestone 1 is a Roslyn
analyzer that enforces `[MustNotAllocate]` across direct method calls.

See `docs/superpowers/specs/` for design, `docs/superpowers/plans/` for the
implementation plan.
