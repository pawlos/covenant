# Short-form variants of the writeup

Three formats. All derive from `draft.md` but each optimized for its platform's register.

---

## Twitter / X thread (11 tweets)

**1/11**

Ferrous Systems published a lovely article last month about the callgraph-closure lint Ferrocene built for IEC 61508 certification.

As I finished reading, I had one concrete thought:

*Wait — .NET already ships this.*

🧵

**2/11**

The attribute you've been suppressing — `[RequiresUnreferencedCode]` — isn't a trimmer wart. It's a generalizable two-pass callgraph-closure analyzer, hardcoded for one predicate.

The framework is in there. Microsoft just didn't say what it is out loud.

**3/11**

The shape: edit-time Roslyn analyzer (fast, limited to the current compilation) + post-build ILLink pass (slow, sees the realized callgraph).

Same shape as Ferrocene's pre-mono THIR lint + post-mono MIR lint.

Not a coincidence. It's the natural shape of the problem.

**4/11**

So I spent a week generalizing it.

1000 lines of C# — roughly 500 Roslyn + 500 Cecil — and you get a property-agnostic callgraph-closure lint that enforces whatever predicate you configure.

Demo property: `[MustNotAllocate]` for hot-path code.

**5/11**

Concrete test: HTTP request-line parser, two variants.

Naive (strings + `.Split` + class-valued results) → 977 diagnostics from the tool at build + IL time.

Optimized (`ReadOnlySpan<byte>` + `ArrayPool<byte>` + `ref struct` result) → 0 diagnostics.

**6/11**

Benchmarks:

```
Parse:  53.1 ns / 280 B  →  6.9 ns / 0 B   (7.7× faster)
Read:   257 ns / 4704 B  →  26 ns / 64 B   (9.7× faster)
```

Qualitative win (analyzer silent) + quantitative win (order of magnitude faster, zero alloc). Both stories close on the same rewrite.

**7/11**

The two-pass architecture earns its keep when calls cross assembly boundaries.

Edit-time Roslyn says: *"CGC002 on `Console.WriteLine` — I can't tell what it does."*

Post-build Cecil walks the realized callgraph and says: *"CGC003, here's the 5-frame chain into BCL that actually allocates."*

**8/11**

Real pain point: `ArrayPool.Rent` allocates transitively but is semantically amortized. You can't just ban it.

Solution: a second attribute, `[AmortizedAllocation]`, that terminates the walker at pool boundaries. Plus a JSON file listing BCL methods (`IndexOf`, `Slice`, etc.) that follow the same pattern.

**9/11**

Adding your own property is ~50 lines. Example, the whole `[MustNotThrow]` implementation:

- Attribute (5 lines)
- Sink: `instruction.OpCode == OpCodes.Throw ? "throw" : null`
- Analyzer binding: attribute FQN + sink list

Shared infrastructure does the rest.

**10/11**

Honest limits, same as Ferrocene's: virtual dispatch (needs CHA or DAM-style annotations), generics without AOT specialization, expression trees, reflection.

None are project-killers. All match gaps the trimmer already admits.

Differential fuzzing Roslyn vs IL is the principled validation story. Weekend project.

**11/11**

Full code, specs, plans, benchmark results:

github.com/<TBD>/dotnet-callgraph-closure

Tagged snapshots for each milestone. Longer writeup here: [blog link].

The framework was already in .NET. Someone just had to say so.

---

## LinkedIn post (~800 words)

**The .NET trimmer is a callgraph analyzer. Most of us haven't noticed.**

A few weeks ago I read Ferrous Systems' article on the callgraph-closure lint Ferrocene built to satisfy IEC 61508 certification for their Rust compiler. The mechanism is simple: attach a custom attribute to a function marking it as "validated," then have the compiler emit a diagnostic any time an unvalidated function calls a validated one. Run this at two phases — pre-monomorphization (fast edit-time feedback) and post-monomorphization (sound coverage of the realized callgraph) — and you get a tool that enforces a boundary the certification evidence depends on.

It's a beautiful technique. And as I finished the article, I realized something uncomfortable: **.NET already ships the entire architecture, in production, for a different predicate.** The warning you've been silencing — `[RequiresUnreferencedCode]` — *is* a callgraph-closure lint. The ILLink pass *is* the post-link sound-coverage half. The only thing hardcoded is which attribute the implementation looks for.

Which means we can generalize it. Here's what I built:

**M1: Roslyn analyzer (~500 lines).** An abstract `CallgraphClosureAnalyzer` base class that takes a configuration: the attribute's fully-qualified name, the propagation direction, a list of `IOperation` predicates ("sinks") that count as ground-truth violations. A concrete subclass wires in `[MustNotAllocate]` plus three sinks — `IObjectCreationOperation` for reference types, `IArrayCreationOperation`, and `IConversionOperation` with `IsBoxing = true`. Apply the attribute to a method, and the IDE lights up wherever it allocates. Works identically in Visual Studio and Rider.

**M2: Cecil-based IL post-pass (~500 lines).** Same two-layer split, but over the compiled binary. Walks the callgraph transitively across assembly boundaries. Its headline feature: taking a Roslyn edit-time diagnostic that says *"I can't tell what `Console.WriteLine` does"* and upgrading it to *"CGC003 (object allocation), 5 frames deep, via `get_Out` → `Volatile.Read` → `Unsafe.AsRef`."* Concrete evidence you couldn't produce with edit-time information alone.

**M2.5: escape hatch + showcase + benchmarks.** The pure-allocation lint would falsely flag `ArrayPool<T>.Rent` because it allocates internally on cold paths. I added a second attribute, `[AmortizedAllocation]`, that terminates the walker at "this method's allocations don't count against callers." Plus a JSON annotations file for BCL methods (`IndexOf`, `Slice`, `SequenceEqual`) that follow the same pattern.

The demonstration: an HTTP request-line parser. The naive implementation (`string.Split`, `Substring`, class-valued result) trips 977 diagnostics from the combined tool. The rewrite to `ReadOnlySpan<byte>` + `ArrayPool<byte>` + `readonly ref struct` trips zero.

Benchmark delta on the parse method:

| | Mean | Allocated |
|---|---:|---:|
| Naive | 53.1 ns | 280 B |
| Optimized | 6.9 ns | 0 B |

**7.7× faster. Zero allocations per call.** The analyzer said the rewrite was correct; the benchmark said it was measurably better. Both stories closing simultaneously is the thing that makes this particular exercise worth anyone's time.

To add your own property — `[MustNotThrow]`, `[MustNotBlock]`, `[RealtimeSafe]`, whatever matters in your codebase — you need three things: the attribute type (five lines), a sink implementation (one or two, depending on the property), and the analyzer binding (ten lines). The shared core handles everything else.

**What I'm not claiming.** This isn't novel research: Ferrocene published the algorithm; Microsoft shipped the infrastructure; I just noticed the shapes match. `[MustNotAllocate]` isn't a good idea for idiomatic C# without the amortized escape hatch — the predicate is a demo, not a recommendation. And there are honest limits: virtual dispatch, generics without AOT, expression trees, reflection. Same gaps Ferrocene's article admits.

**What I am claiming.** The two-pass callgraph-closure pattern is infrastructure, not a trimmer implementation detail. For teams writing real-time audio, game engine update loops, packet-parsing fast paths, or anything in a certification-sensitive context, the gap between "read the Ferrocene article" and "have this working for your property on your codebase" is a weekend. The framework was already here.

Full implementation, milestone tags, design docs, and benchmark results: github.com/<TBD>/dotnet-callgraph-closure

Longer technical writeup with the actual code: [blog link]

If this is useful to anyone's team, I'd like to hear about it.

---

## Hacker News submission

**Title (use exactly):**
> The .NET Trimmer Is a Callgraph Analyzer. Here's How to Make It Enforce Your Invariants.

**First-response comment (or self-post text, ~280 words):**

Context for HN: Ferrous Systems published an article recently about the callgraph-closure lint they built into Ferrocene for IEC 61508 certification. The mechanism is an attribute + a two-pass compiler lint (pre-monomorphization + post-monomorphization) that enforces "no unvalidated function reachable from validated code."

My claim in this piece is that .NET already ships the same architecture under a different name: `[RequiresUnreferencedCode]` and the ILLink pass are structurally identical to Ferrocene's edit-time and post-link lints. The code paths are genericized, it's just that Microsoft ships them with one specific predicate and doesn't name the framework. Generalizing it to arbitrary user-defined properties takes about 1000 lines of C#.

The demo is `[MustNotAllocate]` applied to an HTTP request-line parser. The naive variant (strings + `.Split` + class-valued result) produces 977 diagnostics; the `ReadOnlySpan<byte>` rewrite produces zero, with benchmark results of 7.7× faster parse and zero per-call allocation. Both the analyzer ("is the rewrite correct?") and the benchmark ("is it actually faster?") answer positively on the same commit.

Honest limits documented in the article: virtual dispatch without class-hierarchy analysis, ref generics without AOT specialization, expression trees, reflection. Same gaps Ferrocene's article admits. Differential fuzzing Roslyn output vs Cecil output is the validation strategy I haven't built yet.

I am not claiming novelty on the algorithm (Ferrocene has that) or on the infrastructure (Microsoft has that). I am claiming that the combination — "use the trimmer's architecture for your own predicates" — isn't being talked about, and it should be.

Repo, tagged snapshots per milestone, specs, plans, and benchmark results: [repo URL]

---

## Notes for posting order

- **Long writeup first** (blog / Medium / dev.to / company blog).
- **Hacker News** immediately after long writeup goes live — title verbatim, link to the canonical long-form.
- **LinkedIn post** same day as HN, linking to the blog.
- **Twitter/X thread** within 24 hours of blog going live, linking from tweet 11 to the blog post.

Staggering these means HN and LinkedIn don't compete for the first-hour attention; Twitter is reach amplification after the substantive traffic.

If HN picks it up for the front page, the thing to have ready is a fast, honest comment thread — the article's "what I'm not claiming" paragraph is deliberately structured to front-load the limits that HN will ask about.

## What still needs to be done before any of this posts

1. ~~Confirm the ILLink analyzer class name~~ — done. Correct name is `RequiresAnalyzerBase` (abstract) with three concrete subclasses. §2 of the long draft updated (2026-04-19).
2. ~~Verify the Ferrocene article URL~~ — done. Correct URL is `https://ferrous-systems.com/blog/callgraph-analysis/` (article title: "Callgraph analysis"). Updated (2026-04-19).
3. Fill in the repo URL placeholder (`<TBD>`) in both `draft.md` and `social.md`.
4. Add two IDE screenshots (VS + Rider with squiggles; Naive project's Error List).
5. One proof-reading pass by a human whose first reaction to "callgraph closure" isn't "yes, obviously."
