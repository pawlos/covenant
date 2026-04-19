# The .NET Trimmer Is a Callgraph Analyzer. Here's How to Make It Enforce Your Invariants.

> Draft — sections 1 and 2 of ~10. Audience: .NET developers who have written at least one Roslyn analyzer or hit a `RequiresUnreferencedCode` warning in the wild.

---

## 1. The prompt that wouldn't let go

A few weeks ago I read Ferrous Systems' article on the [callgraph analysis Ferrocene built for IEC 61508 certification][ferrocene-article]. The mechanism is simple enough that you can describe it in a paragraph: attach a custom attribute to a function to mark it as "validated." If any unvalidated function calls a validated one, emit a diagnostic. Run this at both the pre-monomorphization and post-monomorphization phases of the compiler, so you catch violations both at edit time (fast feedback for developers) and after generics have been specialized (sound for the final binary). Apply the attribute to every function in `core` that the validation evidence covers, and now the compiler enforces a boundary you previously had to maintain by vigilance.

It's a genuinely elegant technique, and it's the kind of thing that would be a significant engineering effort to retrofit into any large toolchain. Which is why, as I finished the article, I had one surprisingly concrete thought:

*Wait — we already have this.*

Not "we could build this." Not "there's a paper about this." **.NET already ships the entire architecture, in production, for a different predicate.** It's called `RequiresUnreferencedCodeAttribute`, and most of us have reacted to its warnings by suppressing them. That reaction is accurate about the specific use case (trimming, which is fraught) and completely misses what's actually being shipped: a *generalizable* two-pass callgraph-closure analyzer that happens to be hardcoded for one predicate.

This article is the result of pulling on that thread for a week. By the end of it I had:

- A Roslyn analyzer that enforces a custom property `[MustNotAllocate]` across direct calls at edit time. 500 lines of C#.
- A Cecil-based IL post-pass that walks the compiled callgraph transitively and upgrades "I can't tell" signals into concrete findings with full call chains. Another 500 lines.
- An HTTP request-line parser showcase that goes from 977 analyzer diagnostics and a 280 B/call allocation footprint in the naive form, to 0 diagnostics and zero allocations in the optimized form, with BenchmarkDotNet confirming a 7.7× throughput win.
- A second attribute, `[AmortizedAllocation]`, that handles the one pattern a pure allocation lint falsely flags (pool-backed APIs like `ArrayPool<T>.Rent`).
- The sobering realization that the hard part wasn't the analyzer — it was deciding what to do with four specific edge cases that any real-world use will hit.

The claim I'm going to make and defend in this article is this: **the two-pass callgraph-closure pattern is infrastructure, not a trimmer implementation detail.** You can enforce arbitrary user-defined properties — `[MustNotAllocate]`, `[MustNotThrow]`, `[MustNotBlock]`, `[RealtimeSafe]`, `[ValidatedCore]`, whatever matters in your codebase — with a tractable amount of code, standing on top of the same APIs Microsoft uses for trimming. The framework hasn't been named out loud, and that naming is the reason most teams don't realize they could have this today.

Everything that follows is the proof.

---

## 2. The isomorphism

Here's the side-by-side I wish I'd seen when I first read the Ferrocene article:

| | **Ferrocene** | **.NET trimmer** | **This project** |
|---|---|---|---|
| Attribute | `#[rustc_core_stable]` (internal) | `[RequiresUnreferencedCode]` | `[MustNotAllocate]` |
| Edit-time pass | pre-mono THIR lint | Roslyn analyzer in `Microsoft.NET.ILLink.Analyzers` | Custom Roslyn analyzer (M1) |
| Build-time pass | post-mono MIR lint | ILLink's mark-walk pass | Cecil walker over the published DLL (M2) |
| Direct-call rule | Marked caller → unmarked callee is a diagnostic | Unmarked caller → marked callee is a diagnostic (the warning flows the other direction, but structurally identical) | Marked caller → unmarked callee is a diagnostic (matches Ferrocene) |
| Generics handling | Post-mono pass sees realized instantiations | ILLink sees realized instantiations (JIT doesn't actually specialize ref generics, but the mark walk still follows them) | Same (with caveats; see §8) |
| Virtual dispatch | Conservative + DAM-style type annotations | `DynamicallyAccessedMembers` on type parameters | Conservative; precise dispatch deferred (see §8) |
| Method-group conversions | Explicit in the lint | `RequiresUnreferencedCode` on method groups | Handled via `IDelegateCreationOperation` walking |
| Reflection | Annotate-or-accept | Annotate-or-accept | Annotate-or-accept |

Three observations from this table that do most of the work of the article:

**Observation 1: the trimmer's Roslyn analyzer is parameterized by attribute name.** In `dotnet/runtime` under `src/tools/illink/src/ILLink.RoslynAnalyzer/`, there's an abstract class called `RequiresAnalyzerBase`. Three concrete subclasses — `RequiresUnreferencedCodeAnalyzer`, `RequiresDynamicCodeAnalyzer`, and `RequiresAssemblyFilesAnalyzer` — each specialize it primarily by overriding a single property, `RequiresAttributeFullyQualifiedName`, plus a few hooks for message formatting and custom diagnostic wording. The base class handles the actual work: attribute-mismatch checks across overrides and interface implementations, constructor constraints on generic type parameters, entry-point and static-constructor validation, and call-site patterns between annotated and unannotated methods. **The attribute each analyzer looks for is literally a subclass configuration string.** Microsoft's three shipped predicates differ in that one FQN and their diagnostic wording; everything else is shared. Writing a fourth is what this article is about.

**Observation 2: the two-pass architecture was not invented by Ferrocene, and it wasn't invented by Microsoft. It's the natural shape of the problem.** You need edit-time feedback (otherwise your developers resent the tool). You need post-link soundness (otherwise your certification evidence has holes). Those are different phases of compilation with different information available. If your toolchain has both a semantic analyzer and a post-link pass — and every modern toolchain does — you get this shape whether you want it or not. Ferrocene describes it clearly; the trimmer implements it without the ceremony of naming what it is.

**Observation 3: the ILLink pass and ILLink's plugin surface are genuinely generic machinery.** ILLink is built around an extensibility model (`MarkStep`, `MarkHandler`, custom substitution providers) that is explicitly documented to support use cases other than trimming. The `RequiresUnreferencedCode` behavior is implemented *in terms of* that extensibility, not baked into the core. Which means if you want to add your own property, you're not patching the trimmer — you're writing a plugin against the same extension points it uses.

The rest of this article is how to do exactly that, with a concrete property (`[MustNotAllocate]`) and a concrete showcase (an HTTP parser hot path). But before I show code, I want to state clearly what I'm *not* claiming:

- **I'm not claiming this is novel research.** Ferrocene published the algorithm. Microsoft shipped the infrastructure. I just noticed the two shapes match.
- **I'm not claiming you should use this tool I built.** The version in the repo is a proof-of-concept; real use would need more polish than I've put in.
- **I'm not claiming `[MustNotAllocate]` is a good idea for idiomatic C#.** In a moment I'll show why it isn't, without one specific escape hatch. This is a demo predicate, not a recommendation.

What I *am* claiming is that the gap between "read the Ferrocene article" and "have this working for your property on your codebase" is ~1000 lines of code and one weekend of concentration. For the remainder of the article, I'll walk through what those 1000 lines look like.

---

## 3. The design: property-agnostic core, property-specific sinks

The architecture I ended up with is two-layer:

```
┌─────────────────────────────────────────────────┐
│ Core: property-agnostic callgraph walker        │
│  - attribute FQN                                │
│  - propagation direction                        │
│  - sink list (IOperation predicates)            │
│  - [AmortizedAllocation] escape hatch           │
│  Walks method bodies, diagnoses boundaries.     │
└─────────────────────────────────────────────────┘
                    ▲ configured by
┌─────────────────────────────────────────────────┐
│ Property module: the predicate itself           │
│  - the concrete attribute type                  │
│  - the sinks specific to this property          │
│  [MustNotAllocate]: newobj/newarr/box ops       │
│  [MustNotThrow]    (future): throw ops          │
│  [MustNotBlock]    (future): specific APIs      │
└─────────────────────────────────────────────────┘
```

The core's configuration is a single record:

```csharp
public sealed record Config(
    string AttributeFullName,
    PropagationDirection Direction,
    ImmutableArray<ISink> Sinks,
    string? AmortizedAttributeFullName = null,
    string AmortizedFileName = "amortized-methods.json");
```

Attribute lookup is by full metadata name, not `typeof(T)`, because the analyzer targets `netstandard2.0` and Roslyn's `Compilation.GetTypeByMetadataName` operates on string keys that map across assembly boundaries. This is the same lookup pattern the trimmer's own analyzer uses for `RequiresUnreferencedCodeAttribute`. `PropagationDirection` is a reserved hook for "callee taints caller" (Ferrocene's upward-flowing variant) that the M1 implementation hasn't wired yet but the core structure supports.

The interesting abstraction is `ISink`. Every op in a method body gets fed through a list of these:

```csharp
public interface IIlSink  // IL version; Roslyn version takes IOperation
{
    string? Match(Instruction instruction);
}
```

A sink returns a non-null label (e.g., `"object"`, `"array"`, `"boxing"`) when the instruction matches its predicate, otherwise `null`. This is the one thing the property module configures — everything else the core handles.

The core emits three diagnostic IDs:

| ID | Severity | Meaning |
|---|---|---|
| `CGC001` | Warning | Annotated method calls unannotated **source** method. The boundary is visible to this compilation and the unannotated callee is a concrete problem. |
| `CGC002` | Info | Annotated method calls unannotated **external** method. The compilation can't see into the callee; the IL post-pass will resolve or clear this. |
| `CGC003` | Warning | Annotated method contains a property-specific sink (e.g., `newobj` for `[MustNotAllocate]`). Ground-truth violation, independent of any callgraph walking. |

The three-ID split is the structural hook for the two-pass architecture. At edit time, the Roslyn analyzer produces all three but honestly can't resolve `CGC002` (the information is literally not in the compilation). The IL post-pass has the realized callgraph available and either **upgrades** CGC002 to CGC003 with a concrete chain or **clears** it. That upgrade-or-clear operation is the thing you can only do post-link, and it's the reason the two-pass architecture is load-bearing rather than architectural astronautics.

The per-method algorithm is deliberately simple — no explicit closure computation, no graph walk across annotated methods. For each method annotated with the propagating attribute: walk its operations, dispatch each through the sink list, emit CGC003 on matches; check each call target, emit CGC001 or CGC002 if the target is unannotated. Propagation is emergent: when CGC001 fires, the developer annotates the callee; on the next compile, that callee is itself analyzed by the same rule, and *its* unannotated callees become the new diagnostics. This is the same model Roslyn's trimmer analyzer uses. It costs a little diagnostic cascading during adoption but buys dramatic implementation simplicity — no fixpoint loop, no graph data structure.

## 4. The demo predicate: `[MustNotAllocate]`

The attribute type is trivial:

```csharp
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Constructor,
    AllowMultiple = false,
    Inherited = false)]
public sealed class MustNotAllocateAttribute : Attribute { }
```

`Inherited = false` is deliberate. Virtual override semantics for propagating attributes are their own design question (if `Base.Foo` is annotated and `Derived.Foo` is not, does `Derived.Foo` inherit the promise?). The trimmer handles this with explicit annotations per override; we do the same by default. M1 doesn't walk virtual dispatch at all (a non-goal, revisited in §8), so the question is deferred.

The sinks for `[MustNotAllocate]` at the IL level are three opcodes:

- `newobj` where the declaring type is a **reference type**. `new Foo()` where `Foo` is a class allocates; `new Point(1)` where `Point` is a struct doesn't (the `newobj` instruction exists but initializes stack memory).
- `newarr`. Arrays always heap-allocate.
- `box`. Implicit boxing (`object o = 42`), explicit boxing (`(object)42`), and nullable-value-to-reference boxing all compile to the same IL opcode.

The Roslyn-side equivalents use `IOperation` instead of raw IL: `IObjectCreationOperation` with non-value-type target, `IArrayCreationOperation`, and `IConversionOperation` where `CSharpExtensions.GetConversion(conv).IsBoxing` is true. That last one is worth flagging: the language-agnostic `CommonConversion` struct on `IConversionOperation` doesn't expose `IsBoxing` — it's a C#-specific concept, and you have to call the C# extension method on the operation to get a `Microsoft.CodeAnalysis.CSharp.Conversion` struct that does.

Apply `[MustNotAllocate]` to a trivial method with one allocation, and the IDE squiggles immediately — both in Visual Studio and in Rider, both from the same Roslyn analyzer DLL. (Rider requires one `Invalidate Caches` pass on first install; thereafter it Just Works.)

The one thing to get out of the way now: **`[MustNotAllocate]` is a deliberately challenging demo predicate.** C# is a thoroughly allocating language; LINQ, `async`, string concatenation, closures, iterator blocks, and any `StringBuilder`-adjacent idiom all allocate. A pure allocation lint applied to idiomatic C# code would light up like a Christmas tree and get suppressed within a day. The useful scope for this predicate is narrow — hot-path code in audio/DSP, game engines, real-time instrumentation, network-packet fast paths — and even in those contexts the *point* is to catch allocations the developer didn't *intend*, not to prevent any allocation at all.

Which means we need an escape hatch for pooled memory patterns. We'll get to that in §6. First, the centerpiece.

## 5. Two passes in action: the HTTP request-line parser

I needed a concrete workload to validate whether the analyzer would actually help someone write hot-path code. The canonical example of a hot-path parser in .NET is the HTTP request-line parser — it's what Kestrel does on every incoming request, the shape of the problem is widely familiar, and the allocation temptations are obvious.

### Before: the naive implementation

Here's the "I'm just translating the spec directly" version:

```csharp
public static class RequestLineParser
{
    [MustNotAllocate]
    public static NaiveParsedRequest Parse(string line)
    {
        var parts = line.Split(' ');         // allocates string[] + N strings
        if (parts.Length != 3)
            throw new FormatException("Malformed request line");

        var method = parts[0];
        var target = parts[1];
        var version = parts[2];

        string path, query;
        var queryIdx = target.IndexOf('?');
        if (queryIdx >= 0)
        {
            path = target.Substring(0, queryIdx);    // allocates
            query = target.Substring(queryIdx + 1);  // allocates
        }
        else { path = target; query = string.Empty; }

        return new NaiveParsedRequest(method, path, query, version);  // allocates
    }
}
```

Running the analyzer on this produces the exact warnings you'd expect — committed as `Showcase.Http.Naive.expected.txt` in the repo:

```
RequestLineParser.cs(15,19): warning CGC003: Method 'Parse' is annotated
  [MustNotAllocateAttribute] but contains a object allocation
RequestLineParser.cs(35,16): warning CGC003: Method 'Parse' is annotated
  [MustNotAllocateAttribute] but contains a object allocation
RequestReader.cs(15,22): warning CGC003: Method 'ReadNext' is annotated
  [MustNotAllocateAttribute] but contains a array allocation
```

Three warnings at build time. (The paired `RequestReader` reads the line from a stream with `new byte[4096]` and decodes it with `Encoding.UTF8.GetString` — also instrumented, one more allocation we'll fix in the optimized version.)

### After: the `ReadOnlySpan<byte>` rewrite

Here's the parser rewritten against a byte span, returning a `ref struct` result that holds slices into the original buffer:

```csharp
public static class RequestLineParser
{
    [MustNotAllocate]
    public static bool TryParse(ReadOnlySpan<byte> line, out OptimizedParsedRequest result)
    {
        result = default;

        var eol = line.IndexOf((byte)'\r');
        if (eol >= 0) line = line.Slice(0, eol);

        var firstSpace = line.IndexOf((byte)' ');
        if (firstSpace < 0) return false;
        var method = line.Slice(0, firstSpace);

        var rest = line.Slice(firstSpace + 1);
        var secondSpace = rest.IndexOf((byte)' ');
        if (secondSpace < 0) return false;
        var target = rest.Slice(0, secondSpace);
        var version = rest.Slice(secondSpace + 1);

        ReadOnlySpan<byte> path, query;
        var queryIdx = target.IndexOf((byte)'?');
        if (queryIdx >= 0)
        {
            path = target.Slice(0, queryIdx);
            query = target.Slice(queryIdx + 1);
        }
        else { path = target; query = default; }

        result = new OptimizedParsedRequest(method, path, query, version);
        return true;
    }
}
```

Two design decisions show up here. `Parse` becomes `TryParse` because `throw new FormatException(...)` would itself be an allocation — the analyzer would catch it, honestly. Returning `bool` with an `out` parameter is the idiomatic zero-alloc error channel in .NET, and it keeps the analyzer silent for honest reasons. And `OptimizedParsedRequest` is a `readonly ref struct` holding `ReadOnlySpan<byte>` fields, constructed on the caller's stack and tied to the lifetime of the input span. Callers can't accidentally escape the result into a long-lived container because the compiler won't let them.

The analyzer's committed output for this variant, `Showcase.Http.Optimized.expected.txt`, is empty. Zero warnings. The analyzer has verified that the rewrite honors the promise.

### The benchmark

Both variants implement the same logical surface, so I put them head-to-head in BenchmarkDotNet with `[MemoryDiagnoser]`:

```
ParseBenchmarks
| Method    | Mean      | Allocated | Ratio |
|---------- |----------:|----------:|------:|
| Naive     | 53.1 ns   |   280 B   |  1.00 |
| Optimized |  6.9 ns   |     0 B   |  0.13 |

ReadBenchmarks  (parse + 4KB buffer read from MemoryStream)
| Method    | Mean      | Allocated | Ratio |
|---------- |----------:|----------:|------:|
| Naive     | 257.5 ns  |  4704 B   |  1.00 |
| Optimized |  26.5 ns  |    64 B   |  0.10 |
```

Parse is 7.7× faster and allocates zero bytes per call. Read (which owns the 4KB buffer) is 9.7× faster and drops from 4704 B per call to 64 B — and the 64 B is `MemoryStream` measurement overhead, not the buffer itself (that's pooled, covered in the next section).

The qualitative and quantitative stories close simultaneously: **the analyzer says the rewrite is correct, and the benchmarks say it's measurably faster.** If either of those had come back negative, the whole exercise would have been wasted. Both came back positive, which means this particular tool actually does what it claims — on this particular workload, which is small, but representative of the class of hot-path parsers that motivate the exercise.

## 6. The escape hatch: `[AmortizedAllocation]`

A naive reading of §5 would say "great, apply `[MustNotAllocate]` to everything, rewrite to spans, ship." That would be wrong, and the reason it would be wrong is `ArrayPool<byte>.Shared.Rent(4096)`. A pool-backed buffer API is *semantically* allocation-free from the caller's perspective — you rent a buffer, use it, return it, and at steady state the pool has pre-allocated all the buffers you'll need. But inside `Rent`, the first call has to allocate the buffer, future calls might need to grow the pool, and the implementation is full of lazy-initialization logic that doesn't count as "hot-path allocation" in any useful sense.

Applied literally, a `[MustNotAllocate]` check would fire on every call to `Rent` because the transitive walk would reach internal allocations. Users would face three bad options:

- Annotate `Rent` as `[MustNotAllocate]` — a lie, because it can and does allocate.
- Suppress every `Rent` call site — tedious, and the suppression obscures real violations nearby.
- Stop using `ArrayPool` — actively worse for the hot path.

The fix I settled on is a second attribute, `[AmortizedAllocation]`, that terminates the walker at an edge with different semantics:

```csharp
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Constructor,
    AllowMultiple = false,
    Inherited = false)]
public sealed class AmortizedAllocationAttribute : Attribute { }
```

Semantically, `[MustNotAllocate]` means "I promise not to." `[AmortizedAllocation]` means "my allocations amortize to zero at steady state; callers shouldn't count them against their own promise." Mechanically, both attributes hit the same `continue` branch in the walker — stop here, don't recurse, don't diagnose. The distinction is in the attribute name, which documents intent at the call site.

For BCL methods like `ArrayPool<T>.Rent` that you can't modify, the attribute isn't enough — you need a way to mark external methods. I added a JSON annotations file:

```json
{
  "amortized_methods": [
    "T[] System.Buffers.ArrayPool`1::Rent(System.Int32)",
    "System.Void System.Buffers.ArrayPool`1::Return(T[],System.Boolean)",
    "System.Int32 System.MemoryExtensions::IndexOf(System.ReadOnlySpan`1<T>,T)",
    "System.Int32 System.MemoryExtensions::IndexOfAny(System.ReadOnlySpan`1<T>,T,T)",
    "System.Boolean System.MemoryExtensions::SequenceEqual(System.ReadOnlySpan`1<T>,System.ReadOnlySpan`1<T>)",
    "System.ReadOnlySpan`1<T> System.ReadOnlySpan`1::Slice(System.Int32,System.Int32)",
    "System.Int32 System.IO.Stream::Read(System.Byte[],System.Int32,System.Int32)"
  ]
}
```

The Roslyn side picks this up via `<AdditionalFiles>` in the consumer's csproj. The IL CLI takes it as a `--amortized-file` flag. The FQN strings are in Cecil's resolved-method-name format (the `T` is a method generic parameter, not a type name). The empirically-observed effect on the HTTP showcase: the IL CLI's diagnostic count drops from 603 to **0** for the Optimized variant, because the entries above cover the dominant noise sources — `MemoryExtensions.IndexOf` had 661 transitive chains running through it into BCL error-reporting machinery, and amortizing it prunes the whole subtree.

A reasonable objection: isn't this just a glorified suppression list, moved to a config file? Not quite. Suppressions say "ignore this warning"; amortization says "treat this method as a trusted boundary, terminate the walk." The difference matters because the IL pass *does* walk the callees of suppressed methods, but it does *not* walk the callees of amortized ones. Suppressions hide findings; amortization prunes the search space. And because the list is explicit and versioned, you can audit it the way you'd audit any other trust boundary.

## 7. The second pass pays off

Everything in §5 was the Roslyn analyzer — edit-time feedback, single compilation unit. To demonstrate why the IL post-pass matters, consider a simpler program:

```csharp
using MustNotAllocate;

while (true) { Tick(42); }

[MustNotAllocate]
static void Tick(int sample)
{
    System.Console.WriteLine(sample);  // external
    var scratch = new int[16];         // sink
    _ = scratch;
}
```

The Roslyn analyzer produces two diagnostics:

```
Program.cs(17,9): warning CGC002: Method 'Tick' is annotated
  [MustNotAllocateAttribute] but calls external method 'WriteLine' whose
  annotation status cannot be verified at edit time. This will be
  resolved by the IL post-pass.
Program.cs(20,23): warning CGC003: Method 'Tick' is annotated
  [MustNotAllocateAttribute] but contains a array allocation
```

The CGC003 is ground truth. The CGC002 is honest but useless: "I can't tell whether `Console.WriteLine` allocates." Without a second pass, the user is left to either trust Microsoft's internal implementation or suppress the warning. Neither is a great outcome.

Running the Cecil-based `cgc-ilcheck` against the compiled output, with `bcl-amortized.json` loaded, produces the CGC003 for the array plus a transitive-chain-resolved upgrade for the `Console.WriteLine` call:

```
Method System.Void HotLoop::Tick(System.Int32):
  [CGC003] object allocation (upgraded from CGC002)
    -> HotLoop::Tick
    -> System.Console::WriteLine
    -> System.Console::get_Out
    -> System.Threading.Volatile::Read<System.IO.TextWriter>
    -> System.Runtime.CompilerServices.Unsafe::AsRef<T>
  [CGC003] array allocation
    -> HotLoop::Tick
```

Five frames deep, the IL pass found a concrete allocation inside `Console.WriteLine`'s resolution path. The edit-time "I can't tell" became a post-build "yes, here's exactly where." This is the *reason* the two-pass architecture is more than ceremony. The Roslyn pass has speed (runs on every keystroke) but limited information (can't walk into BCL). The IL pass has full information but is slow enough that you only want it at build time. Neither alone gives you both edit-time feedback and post-build soundness. Together, they do.

The gap between "CGC002 from Roslyn" and "CGC003 from Cecil" is exactly the gap Ferrocene's article spends its third section explaining: pre-mono THIR misses cases that post-mono MIR catches, and that delta is precisely what the post-build pass exists to close.

## 8. Honest limits

Four cases where the current tool falls short, all documented in the repo's `known_issues.md` and matched against the equivalent gaps in Ferrocene's lint and the trimmer:

**Virtual dispatch.** If an annotated method calls `someInterface.DoThing()`, and twelve types implement `DoThing`, the walker has to decide which bodies to consider. M1 takes the cowardly path of looking only at the declared method (the interface method), which has no body, so no transitive analysis is possible. The trimmer handles this with `DynamicallyAccessedMembers` annotations on type parameters — a parallel system for declaring "this type will be instantiated, include its members." Retrofitting that onto the callgraph-closure lint is M3 work and isn't trivial.

**Generics without monomorphization.** Reference-generic instances share IL at runtime (`List<string>.Add` and `List<object>.Add` are the same method body); value-generic instances get duplicated only under NativeAOT. The IL pass walks the shared body, which means it conservatively analyzes based on the generic definition, not the instantiation. For some properties this is fine; for others (e.g., value types that might or might not box when passed through generic interfaces) it isn't. NativeAOT gives you true specialization and is the principled answer, but integrating against the AOT output is its own scope.

**Expression trees.** `Expression<Func<int, int>>` bodies aren't executed by the containing method — they're data, compiled by a visitor into a runtime factory. The containing method never calls the expression body at IL level, so the walker legitimately sees nothing. A real solution walks the `Expression` tree with different semantics (it's a data structure, not a callgraph). The current tool ignores this entirely.

**Reflection.** Any call through `MethodInfo.Invoke`, dynamic code generation, serialization frameworks, or DI containers is opaque. The trimmer's answer is "annotate the relevant surfaces with `DynamicallyAccessedMembers`." The same answer applies here. If you care about reflection-heavy code paths, this is work you'll do anyway.

A thing I want to call out as a validation strategy rather than a limit: **the gap between Roslyn output and IL output is fuzzable.** Generate random valid C# source, compile it, run both passes, diff the diagnostic lists. Anywhere the two pass diverge is either a bug in one of them or a documented limit. SharpFuzz + a C# generator is a weekend project, and the finding rate would be high because the surface is legitimately complicated. That differential harness is the honest answer to "how do you know this thing is right?" I haven't built it yet. It's on the list.

## 9. Add your own property

The infrastructure isn't useful unless adding a new property is cheap. Here's the entire implementation of `[MustNotThrow]`, start to finish:

**The attribute:**

```csharp
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Constructor,
    AllowMultiple = false,
    Inherited = false)]
public sealed class MustNotThrowAttribute : Attribute { }
```

**The IL sink** (`throw` is a single opcode):

```csharp
public sealed class ThrowSink : IIlSink
{
    public string? Match(Instruction instruction) =>
        instruction.OpCode == OpCodes.Throw ? "throw" : null;
}
```

**The analyzer binding:**

```csharp
public static class MustNotThrowIlAnalyzer
{
    public const string AttributeFullName =
        "CallgraphClosure.Attributes.MustNotThrowAttribute";

    public static ImmutableArray<IIlSink> Sinks { get; } =
        ImmutableArray.Create<IIlSink>(new ThrowSink());
}
```

That's the whole thing. The Roslyn-side equivalent is another thirty lines: a `ThrowOperationSink` matching `IThrowOperation`, and a concrete `DiagnosticAnalyzer` subclass that passes the attribute FQN + sink list to the shared `CallgraphClosureAnalyzer` base. The `CallgraphClosure.Core` infrastructure — the walker, the Config record, the diagnostic descriptors, the JSON annotations parser — is reused unchanged.

Apply the new attribute:

```csharp
[MustNotThrow]
public bool TryValidate(ReadOnlySpan<byte> input)
{
    if (input.Length == 0)
        throw new ArgumentException("empty input");  // CGC003 fires here

    return input[0] == 0xFF;
}
```

The analyzer flags the `throw` statement inside the `[MustNotThrow]` method. You refactor to a bool return:

```csharp
[MustNotThrow]
public bool TryValidate(ReadOnlySpan<byte> input)
{
    if (input.Length == 0) return false;
    return input[0] == 0xFF;
}
```

Silent. The contract is now mechanically enforced. The same reasoning applies to `[MustNotBlock]` (sink list: specific thread-blocking APIs like `Task.Wait`, `Thread.Sleep`, `Monitor.Enter`), `[MustNotLog]` (sink list: your logging framework's emit methods), and anything else where the question "does this method reach a specific kind of operation" is well-formed.

The cost of adding a new property is dominated by the sink-set design — figuring out which IL opcodes or IOperation patterns mean what you mean. The infrastructure is free.

## 10. Try it

The full implementation is at [github.com/<TBD>/dotnet-callgraph-closure]. Tagged snapshots:

- `m1-complete` — Roslyn analyzer only, direct calls
- `m2-complete` — Cecil IL post-pass added, transitive walking
- `m2.5-complete` — `[AmortizedAllocation]` + JSON annotations + HTTP showcase + BenchmarkDotNet

Design docs live in `docs/superpowers/specs/`. Implementation plans (one per milestone) live in `docs/superpowers/plans/`. `bench/Showcase.Http.Benchmarks/baseline-results.md` has the numbers quoted in §5 plus the exact command to reproduce them.

To run the analyzer against your own code, reference the `MustNotAllocate` analyzer project in your csproj as `OutputItemType="Analyzer"`, apply `[MustNotAllocate]` to a method, and watch what the IDE shows you. To run the IL CLI against a compiled DLL:

```bash
dotnet run --project src/CallgraphClosure.ILCheck.Cli/ -- \
  --amortized-file src/MustNotAllocate.ILCheck/bcl-amortized.json \
  path/to/your/assembly.dll
```

The one real rough edge is analyzer packaging: the sample project uses an absolute-path `<Analyzer>` workaround to get the analyzer loaded alongside its attribute, which is documented in `known_issues.md`. The principled fix is splitting the attribute into a separate library that isn't flagged as a Roslyn component; I haven't done it yet.

**The thesis, once more, because it's the only thing I'd ask you to remember:** the two-pass callgraph-closure architecture Ferrocene wrote about is already infrastructure in .NET, hiding under `RequiresUnreferencedCode`. It works for arbitrary user-defined properties, not just trimming. It takes about a thousand lines of code to wield. And the predicates you might want to enforce — allocation, exception flow, blocking, logging boundaries — are mostly designing the right sink set, which is the interesting problem anyway.

The framework was already here. Someone just had to say so.

[ferrocene-article]: https://ferrous-systems.com/blog/callgraph-analysis/
