# The .NET Trimmer Is a Callgraph Analyzer. Here's How to Make It Enforce Your Invariants.

> Draft — sections 1 and 2 of ~10. Audience: .NET developers who have written at least one Roslyn analyzer or hit a `RequiresUnreferencedCode` warning in the wild.

---

## 1. The prompt that wouldn't let go

A few weeks ago I read Ferrous Systems' article on the [callgraph-closure lint Ferrocene built for IEC 61508 certification][ferrocene-article]. The mechanism is simple enough that you can describe it in a paragraph: attach a custom attribute to a function to mark it as "validated." If any unvalidated function calls a validated one, emit a diagnostic. Run this at both the pre-monomorphization and post-monomorphization phases of the compiler, so you catch violations both at edit time (fast feedback for developers) and after generics have been specialized (sound for the final binary). Apply the attribute to every function in `core` that the validation evidence covers, and now the compiler enforces a boundary you previously had to maintain by vigilance.

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

**Observation 1: the trimmer's analyzer *is* a callgraph-closure lint.** The Microsoft-shipped `ILLink.Analyzers` package has an abstract class called `RequiresAttributeMismatchAnalyzer` that walks method bodies looking for calls where the caller lacks an attribute and the callee has it. If you replace "RequiresUnreferencedCode" with "your attribute" throughout the source, you get exactly the edit-time half of a Ferrocene-style lint. Microsoft wrote it. It's in the reference source. The only thing hardcoded is which attribute it looks for.

**Observation 2: the two-pass architecture was not invented by Ferrocene, and it wasn't invented by Microsoft. It's the natural shape of the problem.** You need edit-time feedback (otherwise your developers resent the tool). You need post-link soundness (otherwise your certification evidence has holes). Those are different phases of compilation with different information available. If your toolchain has both a semantic analyzer and a post-link pass — and every modern toolchain does — you get this shape whether you want it or not. Ferrocene describes it clearly; the trimmer implements it without the ceremony of naming what it is.

**Observation 3: the ILLink pass and ILLink's plugin surface are genuinely generic machinery.** ILLink is built around an extensibility model (`MarkStep`, `MarkHandler`, custom substitution providers) that is explicitly documented to support use cases other than trimming. The `RequiresUnreferencedCode` behavior is implemented *in terms of* that extensibility, not baked into the core. Which means if you want to add your own property, you're not patching the trimmer — you're writing a plugin against the same extension points it uses.

The rest of this article is how to do exactly that, with a concrete property (`[MustNotAllocate]`) and a concrete showcase (an HTTP parser hot path). But before I show code, I want to state clearly what I'm *not* claiming:

- **I'm not claiming this is novel research.** Ferrocene published the algorithm. Microsoft shipped the infrastructure. I just noticed the two shapes match.
- **I'm not claiming you should use this tool I built.** The version in the repo is a proof-of-concept; real use would need more polish than I've put in.
- **I'm not claiming `[MustNotAllocate]` is a good idea for idiomatic C#.** In a moment I'll show why it isn't, without one specific escape hatch. This is a demo predicate, not a recommendation.

What I *am* claiming is that the gap between "read the Ferrocene article" and "have this working for your property on your codebase" is ~1000 lines of code and one weekend of concentration. For the remainder of the article, I'll walk through what those 1000 lines look like.

---

<!-- TODO sections 3-10 -->

[ferrocene-article]: https://ferrous-systems.com/blog/rustc-callgraph-closure-lint/
