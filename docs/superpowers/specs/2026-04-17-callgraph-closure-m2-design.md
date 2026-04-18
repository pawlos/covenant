# Callgraph-Closure Lint for .NET — Milestone 2 Design

**Status:** Approved (2026-04-17)
**Scope:** Cecil-based IL post-pass that validates the same `[MustNotAllocate]` property as M1, but across assembly boundaries. Delivers the "two-pass story" headline.

## Purpose

M1 gave us the Roslyn analyzer. Its primary soft spot is CGC002 — calls from annotated methods into external BCL code whose IL the compilation cannot see. M2's purpose is to upgrade those CGC002s: load the compiled sample plus its dependencies, walk the realized IL transitively, and either find a concrete allocation (CGC001-equivalent with a call chain) or clear the warning.

**Writeup headline:** run M1 on the sample → CGC002 on `Console.WriteLine`. Run M2 on the compiled output → concrete allocation found three frames deep inside `System.IO.TextWriter`. Same infrastructure, different lens.

## Inherited from M1 (no re-discussion)

- Same three diagnostic IDs (CGC001 / CGC002 / CGC003), same semantics.
- Same `[MustNotAllocate]` attribute as the demo property.
- Same downward-propagation direction.
- Same property-agnostic core + property-specific sinks architectural split.
- Same acceptance of duplicate diagnostics.
- xUnit test harness with compiled-sample fixtures.

## Decisions locked in during brainstorm

1. **Tool shape:** standalone .NET console CLI (`dotnet run -- path/to/sample.dll`). Not an MSBuild task, not a library-only consumer. Keeps the writeup's "run both, diff outputs" story crisp.
2. **Scope boundary:** MVP does **transitive BCL walk only**. Delegates, method-group-to-function-pointer conversions, async/iterator state machines, generic value-type specialization — all explicitly deferred. They're real IL wins but independent tasks; the transitive-walk case alone proves the two-pass claim.
3. **Output format:** human-readable text for the PoC. SARIF / Roslyn-JSON compatibility is deferred until the tool actually ships.

## Pre-work: M1 TFM cleanup

Before M2 proper, upgrade all non-analyzer projects from net8.0 to net10.0 so the whole solution shares a single current-LTS TFM. Analyzer projects (`CallgraphClosure.Core`, `MustNotAllocate`) stay on netstandard2.0 — that's a Roslyn SDK requirement, unchanged.

Affected files:
- `src/MustNotAllocate.Sample/MustNotAllocate.Sample.csproj` — `<TargetFramework>net8.0</TargetFramework>` → `net10.0`
- `tests/MustNotAllocate.Tests/MustNotAllocate.Tests.csproj` — same
- `tests/MustNotAllocate.Tests/CSharpAnalyzerVerifier.cs` — `ReferenceAssemblies.Net.Net80` → `ReferenceAssemblies.Net.Net100` (verify the constant exists in the pinned `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit 1.1.2`; if not, either upgrade the package or use a generic `ReferenceAssemblies` constructor pointing at net10 refs)

This is the plan's first task, before any M2 code.

## Architecture

### Package layout

```
src/
  CallgraphClosure.ILCheck.Core/        net10.0 library
    Config.cs                           attribute FQN + sinks + direction
    IIlSink.cs                          IL-level sink abstraction
    ClosureWalker.cs                    transitive IL walk, cycle detection
    Diagnostic.cs                       IL-diag record (id, caller, chain, sink)
    AssemblyResolver.cs                 Cecil resolver with framework-ref support

  MustNotAllocate.ILCheck/              net10.0 library
    MustNotAllocateIlAnalyzer.cs        binds attribute FQN + IL sinks
    Sinks/
      NewObjSink.cs                     newobj on reference type
      NewArrSink.cs                     newarr
      BoxSink.cs                        box instruction

  CallgraphClosure.ILCheck.Cli/         net10.0 exe
    Program.cs                          CLI entry: parse args → run → print

tests/
  MustNotAllocate.ILCheck.Tests/        xUnit on compiled fixture DLLs
```

Three projects, same two-layer split as M1 (reusable core + concrete property module), plus a thin CLI. Tests target the property module.

### Data flow

```
                                    +---------------------------+
  path/to/sample.dll  ─────────────▶│  CallgraphClosure.ILCheck │
                                    │         .Cli              │
                                    +---------------------------+
                                                │
                                                ▼
                                    load via Cecil + AssemblyResolver
                                                │
                                                ▼
                                    +---------------------------+
                                    │ ClosureWalker.Analyze()   │
                                    │  for each annotated M:    │
                                    │    walk IL callgraph      │
                                    │    record sink hits       │
                                    │    resolve calls across   │
                                    │    assembly boundaries    │
                                    +---------------------------+
                                                │
                                                ▼
                                    List<Diagnostic>  ────▶  stdout (human-readable)
```

### Algorithm sketch (ClosureWalker)

For each method annotated with the propagating attribute:

1. Maintain a `HashSet<MethodReference>` of visited methods (cycle guard).
2. Depth-first walk starting from the annotated method:
   a. For each IL instruction in the method body:
      - If it matches a registered `IIlSink`: record a `Diagnostic` with the current call chain.
      - If it's `call` / `callvirt` / `newobj`: resolve the target `MethodReference` → `MethodDefinition`. If already visited or unresolvable (virtual w/o CHA, missing assembly), skip. Else recurse.
   b. When recursing, push the callee onto the call-chain stack; pop on return.
3. Annotated callees terminate the walk at that edge (they make the same promise; no need to re-verify their bodies).

**Cycle handling:** `MethodReference`-based visited set. Covers mutual recursion and self-recursion cleanly.

**Unresolvable calls:** `callvirt` where CHA would be required (no implementations loaded), or `call` to a method whose declaring assembly isn't on the resolver's path, emit a `CGC002` in the output — same signal M1 produces, meaning "this tool also can't tell."

### Assembly resolution

Cecil's `DefaultAssemblyResolver` looks in the GAC and the input assembly's directory. For net10.0 inputs we also need the shared framework ref path.

MVP strategy: require the user to point at a **publish output directory** (`dotnet publish`'s output), which contains the input DLL plus every dependency the runtime needs. Our resolver's search paths = the publish dir + the net10.0 shared framework path (discovered from `dotnet --list-runtimes` or a config file). No `.deps.json` parsing needed for the PoC.

### Sinks (property-specific, for `[MustNotAllocate]`)

Same conceptual set as M1, at the IL opcode level:

| Sink | Matches | Label |
|---|---|---|
| `NewObjSink` | `newobj` where the constructor's declaring type is a reference type | `"object"` |
| `NewArrSink` | `newarr` | `"array"` |
| `BoxSink` | `box` | `"boxing"` |

Value-type constructor calls (`newobj Struct::.ctor`) still fire CGC001-equivalents for the ctor edge if the ctor itself is unannotated, but NOT a CGC003 — same rule as M1.

### Diagnostic record

```csharp
public sealed record Diagnostic(
    string Id,                              // "CGC001", "CGC002", "CGC003"
    string PropertyName,                    // "MustNotAllocate"
    MethodDefinition AnnotatedCaller,       // entry point
    ImmutableArray<MethodReference> Chain,  // caller → ... → target (innermost last)
    string? SinkLabel,                      // "object"/"array"/"boxing" for CGC003, null otherwise
    MethodReference? UnresolvedTarget);     // for CGC002 fallback
```

### Output format (text, stdout)

```
=== CallgraphClosure IL Check ===
Input: /path/to/sample.dll
Annotated methods found: 1

Method HotLoop.Tick(System.Int32):
  [CGC003] array allocation
    HotLoop.Tick                  Program.cs:20
      → newarr System.Int32
  [CGC003] object allocation (upgraded from CGC002)
    HotLoop.Tick                  Program.cs:17
      → System.Console.WriteLine(System.Int32)
      → System.Console.Out.WriteLine(System.Int32)
      → System.IO.TextWriter.WriteLine(System.Int32)
      → newobj System.Text.StringBuilder::.ctor

Summary: 1 CGC003 direct, 1 CGC003 upgraded, 0 CGC002 unresolved.
```

Exit code: 0 if no diagnostics, 1 otherwise. Matches typical lint-tool conventions.

## Testing strategy

Analyzer tests compile a fixture C# source to a DLL at test setup, then run `ClosureWalker.Analyze` over the compiled assembly and assert on the emitted diagnostics.

Fixture categories:

1. **Mirror of M1 positives** — `[MustNotAllocate]` method with direct `newobj`, `newarr`, `box` → same diagnostics as M1 produces, verifying the IL pass is at least as precise.
2. **Transitive upgrade (the headline case)** — `[MustNotAllocate]` method calls an unannotated method that calls `new Something()`. Roslyn only sees the boundary; IL pass follows the chain and reports CGC003 with the full chain.
3. **Cross-assembly transitive** — fixture calls `System.Console.WriteLine`; assert the walk terminates at *some* sink inside `System.Console.dll` or emits CGC002 if the BCL ref assembly used by the test harness is a ref-only API surface with no method bodies. This is a **known uncertainty**: some BCL packages ship as reference assemblies (throw-only stubs); walking them yields nothing. Spec'd behavior: if the method body is empty or has only `throw null`, emit CGC002 with the target. Revisit with `--runtime-refs` CLI arg if we need true runtime bodies.
4. **Cycle guard** — fixture has mutual recursion `A → B → A`; assert no infinite loop and one diagnostic emitted per distinct sink.
5. **Annotated callee terminates walk** — `[MustNotAllocate] Caller()` calls `[MustNotAllocate] Helper()` which calls `new Foo()` (unannotated). Caller's walk should stop at Helper (Helper made the same promise). The `Helper → new Foo` violation is reported under Helper's own walk, not Caller's.
6. **Unresolvable virtual** — fixture calls a `callvirt` on an interface with no implementations loaded; assert CGC002 emitted, not a crash.

Fixture compilation uses `Microsoft.CodeAnalysis.CSharp` APIs to compile-in-memory to a `Stream`, save to a temp file, then feed the path to Cecil. Shared `CompileFixture` helper.

## Non-goals (M2)

Matches M1's non-goal list; listed here so they're explicit and testable as "no diagnostic" cases.

| Case | Behavior | Deferred to |
|---|---|---|
| Virtual / interface dispatch resolution | `callvirt` treated like unresolvable-external → CGC002 | M3 (CHA or receiver-type flow) |
| Generic value-type specialization | Not attempted — reference JIT doesn't specialize | NativeAOT-only M4 |
| Delegate construction / method-group conversion | `ldftn` followed by `newobj Delegate::.ctor` not tracked | M2.5 |
| `async` / iterator state machines | Walked as ordinary `MoveNext` calls; allocations inside MoveNext flagged naturally. No special handling | covered incidentally |
| Reflection | Opaque | annotate-or-accept |
| Runtime ref assembly walks (reference-only BCL stubs) | CGC002 emitted when method body is empty/throw-only | M2.5 (config flag) |

## Success criteria

M2 is "done" when:

1. All fixture tests pass.
2. Running the CLI against the M1 sample's compiled output produces:
   - The same CGC003 diagnostic for `new int[16]` as M1.
   - A CGC003 (upgraded from CGC002) for the transitively-reached allocation inside `Console.WriteLine`'s implementation, OR a CGC002 if BCL refs are reference-only — either outcome is valid and documented.
3. The difference between Roslyn output (CGC002 on `Console.WriteLine`) and IL output (either concrete CGC003 or resolved CGC002) is demonstrable in the writeup.

## Open uncertainty (flagged, not blocking)

**BCL reference assemblies vs. runtime assemblies.** The net10.0 targeting packs ship reference assemblies (public API surface, method bodies are `throw null`), not runtime assemblies. If Cecil loads these, the transitive walk terminates with no sink found — falsely clearing the CGC002. Workarounds: (a) resolve against the runtime shared-framework dir (`/usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.x/`), where real implementations live; (b) emit CGC002 when a method body is suspicious (single-instruction `throw`), signaling "walked but couldn't verify." MVP will try (a); if it doesn't Just Work, fall back to (b) with a warning. Either is a valid M2 state — this is a deployment/packaging detail, not an algorithm question.

## Post-M2 roadmap (not binding)

1. **M2.5:** delegate / `ldftn` tracking, method-group conversions, runtime-ref vs ref-only handling.
2. **M3:** virtual/interface dispatch via class hierarchy analysis.
3. **M4:** NativeAOT integration — post-compilation binary walk gives true generic specialization.
4. **Differential fuzzing:** SharpFuzz generating C# sources, diff Roslyn vs IL pass outputs.
