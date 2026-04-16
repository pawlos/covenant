# Callgraph-Closure Lint for .NET — Milestone 1 Design

**Status:** Approved (2026-04-16)
**Scope:** Minimal Roslyn analyzer for a single downward-propagating property attribute. Direct method calls only.

## Background

A .NET analog of Ferrocene's callgraph-closure lint (Ferrous Systems blog post, 2024): a custom attribute marks a function with a property, and the property relates to callgraph structure. The direct inspiration is Ferrocene's IEC 61508 use case ("no validated function in core calls an unvalidated one"), but the mechanism generalizes to properties like `[MustNotAllocate]`, `[MustNotThrow]`, `[MustNotBlock]`, `[RealtimeSafe]`.

The architecture mirrors Ferrocene's two-pass design (pre-mono THIR + post-mono MIR), which in .NET corresponds exactly to the trimmer's two-pass layout:

| Ferrocene | .NET trimmer | This project |
|---|---|---|
| pre-mono THIR lint | Roslyn analyzer (`RequiresUnreferencedCodeAttribute`) | Roslyn analyzer (M1) |
| post-mono MIR lint | ILLink pass | ILLink plugin or Cecil tool (M2+) |

**This spec covers Milestone 1 only** — the Roslyn-analyzer half. The ILLink post-pass is a separate milestone with its own design.

M1 exists to get the diagnostic infrastructure, symbol resolution, and attribute lookup working end-to-end on a toy example. Each subsequent capability (virtuals, generics, delegates, expression trees, async) extends this foundation one axis at a time.

## Semantics

### Propagation direction

Two directions are needed eventually; **M1 implements the downward direction**:

- **Downward (M1)**: annotation on caller expresses a promise about the caller's behavior. The constraint flows to callees: an annotated method that calls an unannotated method is a diagnostic (the unannotated callee cannot be verified to uphold the promise). Example: `[MustNotAllocate]`.
- **Upward (future)**: annotation on callee taints callers. Calling an annotated function makes the caller require the property too. This is Ferrocene's original semantics and maps to `[ValidatedCore]`-style use cases. Infrastructure accommodates it via a `Direction` parameter in the core; sink lists swap to "entry-point markers."

### Rule (downward)

Let `M` be a method annotated with the propagating attribute.

- **Direct unannotated call edge.** If `M` contains an `IInvocationOperation` whose `TargetMethod.OriginalDefinition` lacks the attribute: diagnostic.
- **Property-specific sink.** If `M`'s operation tree contains an op matching one of the property's sink patterns: diagnostic.

The two rules are independent. A `new X()` where `X`'s ctor is unannotated fires both — this is accepted duplication in M1 (see §6.4).

### Non-goals (M1)

Explicitly out of scope, listed so the absence of behavior is intentional, not a bug:

| Case | M1 behavior | Deferred because |
|---|---|---|
| Virtual / interface dispatch | Analyzes static target only; overrides unchecked | Sound-vs-noisy tradeoff needs its own design |
| Generic specialization at call sites | `OriginalDefinition` lookup only | True closure needs monomorphization — that's the IL pass's job |
| Method group conversions / delegate creation | Silent | Needs `IDelegateCreationOperation` handling + delegate type-flow |
| Expression trees | Silent | `Expression<Func<...>>` body isn't executed by containing method |
| `async` / `await` | Analyzes original body, not `MoveNext` | Continuation visibility via IOperation needs verification |
| Reflection | Opaque | Annotate-or-accept convention, same as trimmer |
| Upward propagation | Core accepts the parameter but no module wires it | Separate milestone |
| IL post-pass | Out of scope | Own milestone; M1 is the Roslyn side of the two-pass story |

## Architecture

### Two-layer split

```
┌─────────────────────────────────────────────────┐
│ Core: property-agnostic callgraph closure       │
│ - Input: attribute FQN + direction + sink list  │
│ - Walks IOperation inside annotated methods     │
│ - Diagnoses boundary crossings and sink hits    │
└─────────────────────────────────────────────────┘
                       ▲
                       │ configured by
┌─────────────────────────────────────────────────┐
│ Property module: property-specific sinks        │
│ - [MustNotAllocate]: object/array creation,     │
│   boxing conversions                            │
│ - [MustNotThrow]  (future): throw ops           │
│ - [MustNotBlock]  (future): specific API calls  │
└─────────────────────────────────────────────────┘
```

**Direction lives in the core.** Adding upward propagation later grows the core's `Direction` parameter; property modules don't change.

**Sinks are property-specific and separable.** The sink list is what distinguishes `[MustNotAllocate]` from `[MustNotThrow]` at the analyzer level.

### Package layout

```
CallgraphClosure.Core/              netstandard2.0, analyzer project
  CallgraphClosureAnalyzer.cs       abstract base : DiagnosticAnalyzer
  ClosureWalker.cs                  property-agnostic algorithm
  ISink.cs, *Sink.cs                sink abstractions and primitives
  Diagnostics.cs                    CGC001 / CGC002 / CGC003 descriptors

MustNotAllocate/                    netstandard2.0
  MustNotAllocateAttribute.cs       attribute type (users reference)
  MustNotAllocateAnalyzer.cs        : CallgraphClosureAnalyzer
                                    binds attribute FQN + allocation sinks

MustNotAllocate.Sample/             net8.0 console exe, toy hot-loop demo
MustNotAllocate.Tests/              net8.0, xUnit + Microsoft.CodeAnalysis.Testing
```

Solution file at repo root; all projects referenced.

### Attribute type

```csharp
namespace MustNotAllocate;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor,
                AllowMultiple = false, Inherited = false)]
public sealed class MustNotAllocateAttribute : Attribute { }
```

`Inherited = false` is deliberate. `INamedTypeSymbol.GetAttributes()` does not walk inheritance, and inherited semantics would be unsound for overrides (base annotation does not constrain what an override can do). Virtual dispatch is handled explicitly in M2.

### Binding the concrete analyzer to the core

Constructor-injected configuration. No reflection, no attribute-discovery magic.

```csharp
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MustNotAllocateAnalyzer : CallgraphClosureAnalyzer
{
    public MustNotAllocateAnalyzer() : base(new Config(
        AttributeFullName: "MustNotAllocate.MustNotAllocateAttribute",
        Direction: PropagationDirection.Downward,
        Sinks: new ISink[]
        {
            new ObjectCreationSink(),
            new ArrayCreationSink(),
            new BoxingConversionSink(),
        })) { }
}
```

Attribute lookup is by full metadata name, not `typeof(T)`. The core is reusable infrastructure — each property module supplies a different attribute FQN at construction time. Hardcoding a `typeof(SomeSpecificAttribute)` in the core would couple it to a specific property. Name-based lookup via `Compilation.GetTypeByMetadataName` resolves the symbol once per compilation start and is the same pattern Roslyn's trimmer analyzer uses for `RequiresUnreferencedCodeAttribute`.

If the attribute type is not present in a compilation (user hasn't referenced the attribute package), the analyzer is a silent no-op. Verified by test.

## Algorithm

M1 is **local per-method**. No graph walk, no closure computation. Propagation is emergent: CGC001 on `A → B` prompts the user to annotate `B`, after which `B`'s own body is analyzed and may produce new diagnostics on `B`'s callees. This mirrors how Roslyn's trimmer analyzer handles `RequiresUnreferencedCode` and is sufficient for direct calls because every call edge is statically visible on the caller side.

### Registration

```csharp
public override void Initialize(AnalysisContext ctx)
{
    ctx.EnableConcurrentExecution();
    ctx.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
    ctx.RegisterCompilationStartAction(OnStart);
}

void OnStart(CompilationStartAnalysisContext c)
{
    var attrSym = c.Compilation.GetTypeByMetadataName(_config.AttributeFullName);
    if (attrSym is null) return;
    c.RegisterOperationBlockAction(b => Analyze(b, attrSym));
}
```

`RegisterOperationBlockAction` covers method bodies, constructors, destructors, property accessors, and local functions — all in one registration.

### Per-block logic

```
for each operation-block in a method M:
    if M is not annotated with attrSym: skip
    for each IOperation op in block.Descendants():
        dispatch(op)
```

### Op dispatch table

| Op kind | Check | Diagnostic |
|---|---|---|
| `IInvocationOperation` | `op.TargetMethod.OriginalDefinition` lacks attr | CGC001 (source callee) or CGC002 (external callee) |
| `IObjectCreationOperation` | sink + ctor annotation check | CGC003 (sink) + CGC001/002 on ctor edge if unannotated |
| `IArrayCreationOperation` | sink | CGC003 |
| `IConversionOperation` where `Conversion.IsBoxing` | sink | CGC003 (boxing) |

**`OriginalDefinition` unwrapping** is mandatory. `Foo<int>()` has `TargetMethod` pointing to the constructed `Foo<int>` symbol; attributes are on `Foo<T>`'s original definition. M1 ignores generic propagation otherwise, but the unwrapping prevents trivially missing annotations on any generic method.

**Source vs external classification**:

```csharp
bool IsExternal(IMethodSymbol target) =>
    !SymbolEqualityComparer.Default.Equals(
        target.ContainingAssembly, compilation.Assembly);
```

Sibling projects in a multi-project solution are "external" by this rule (`ContainingAssembly` differs). This is correct: the boundary for edit-time certainty is the compilation unit. The IL pass operates at solution/publish scope and resolves these cases.

**Boxing detection**: use `IConversionOperation.Conversion.IsBoxing` directly. Do not reinvent the check. This covers implicit (`object o = 42`), explicit (`(object)42`), and nullable-value-to-reference (`object o = (int?)42`) cases uniformly.

### Diagnostic descriptors

All three are in the `CGC` category with severity per the table.

| ID | Severity | Title |
|---|---|---|
| CGC001 | Warning | Annotated method calls unannotated source method |
| CGC002 | Info | Annotated method calls unannotated external method |
| CGC003 | Warning | Annotated method contains a property-specific sink |

Messages (locked):

- **CGC001**: `"Method '{0}' is annotated [{1}] but calls unannotated method '{2}'. Annotate '{2}' or remove the call."`
- **CGC002**: `"Method '{0}' is annotated [{1}] but calls external method '{2}' whose annotation status cannot be verified at edit time. This will be resolved by the IL post-pass."`
- **CGC003**: `"Method '{0}' is annotated [{1}] but contains a {2} allocation."` where `{2}` is `object`, `array`, or `boxing`.

The three-ID split is the structural hook for the two-pass architecture: CGC001 and CGC003 are ground truth at edit time; CGC002 is "can't tell yet" — the ILLink pass (M2+) either upgrades CGC002 to a CGC001-equivalent or clears it based on the realized callgraph. This split is what makes the two passes divide labor in a principled way and gives differential fuzzing a concrete target (any CGC002 the IL pass neither upgrades nor clears is a precision-gap bug).

### Squiggle location

`op.Syntax.GetLocation()` for all three diagnostics. The expression itself gets highlighted, not the whole statement or the method declaration.

### Duplicate-diagnostic policy

`new X()` inside an annotated method where `X`'s constructor is unannotated fires both CGC003 (on the `newobj` allocation) and CGC001/002 (on the ctor call edge). These report the same location from two different axes. **M1 accepts this duplication.** They are semantically distinct: CGC003 says "this expression allocates"; CGC001/002 says "this ctor body cannot be verified." A single `[SuppressMessage]` wouldn't cover both anyway, so deduplication would require a structured suppression API we don't have yet. Revisit if users complain.

## Testing strategy

`Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit` for source-in, expected-diagnostics-out verification.

### Positive cases

- CGC001: annotated calls unannotated source method in same project
- CGC002: annotated calls `Console.WriteLine` (external)
- CGC003: `new object()`, `new int[10]`, `object o = 42`, `(object)42`, `object o = (int?)42`
- Duplicate (source ctor): `new UnannotatedCtor()` where `UnannotatedCtor` is a source type — fires both CGC001 and CGC003 at the same location
- Duplicate (external ctor): `new StringBuilder()` — fires both CGC002 and CGC003 at the same location

### Negative cases

- Annotated → annotated (same project): silent
- Unannotated → unannotated: silent
- Unannotated → annotated (wrong direction for downward): silent
- `[MustNotAllocate] void Caller<T>() { Callee<T>(); }` with `Callee<T>` annotated: silent (OriginalDefinition unwrap works)

### Infra cases

- Analyzer silent when attribute package is not referenced (must not throw)
- Cascading: annotating one method shifts diagnostics to its callees on re-run (verified with progressive-annotation fixtures)

### Deferred test cases (noted, not written for M1)

- Sibling-project call with annotated external (requires multi-project test fixture) — written when we have the need
- Annotated-in-referenced-assembly (requires a dedicated test dependency assembly) — same

### Manual smoke test

`MustNotAllocate.Sample/` contains a toy realtime-ish audio-tick loop with two intentional violations — one allocation, one unannotated call — for IDE-squiggle screenshots in the writeup.

## Post-M1 roadmap (not binding)

1. **M2**: IL post-pass PoC on Cecil over the M1 sample. Compile sample, walk IL, emit the same three diagnostic IDs from the realized callgraph, diff against Roslyn's output. First Roslyn-missed / IL-caught case is the writeup's headline example.
2. **M3**: virtual and interface dispatch in both passes.
3. **M4**: generic specialization (IL pass is primary).
4. **M5**: delegates, method group conversions, expression trees.
5. **Differential fuzzing harness** (parallel track): SharpFuzz-driven C# source generation, compare Roslyn and IL-pass diagnostics for divergence. Precision-gap bugs become writeup case studies.
