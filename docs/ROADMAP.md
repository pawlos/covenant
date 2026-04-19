# Roadmap

As of 2026-04-19. Tags: `m1-complete`, `m2-complete`, `m2.5-complete`.

This document lists next-step options in rough priority order. Not a commitment — a menu. Each item has a scope estimate, dependencies, and the argument for/against doing it. When an item graduates into active work, write a spec for it under `docs/superpowers/specs/`, a plan under `docs/superpowers/plans/`, and update the table below with the tag.

## Where we are

| Concern | State |
|---|---|
| Roslyn analyzer for `[MustNotAllocate]` + direct-call closure | ✅ M1 |
| Cecil IL post-pass with transitive walk | ✅ M2 |
| `[AmortizedAllocation]` escape hatch + JSON annotations file | ✅ M2.5 |
| HTTP request-line parser showcase (Naive vs Optimized) with BenchmarkDotNet | ✅ M2.5 |
| Writeup long-form draft | ✅ `docs/writeup/draft.md` (4600 words) |
| Social variants (Twitter / LinkedIn / HN) | ✅ `docs/writeup/social.md` |
| ILLink class-name + Ferrocene URL fact-checks | ✅ |
| Repo URL fill-in, IDE screenshots, human proofread | ⏳ remaining writeup polish |

## Near-term (this week or next)

### 1. Ship the writeup

**Scope:** ~2-4 hours of polish + publishing ceremony.

**What's left before posting:**
- Fill the `<TBD>` repo URL placeholder in both `draft.md` and `social.md` (blocked on deciding where the repo lives — GitHub user/org).
- Two IDE screenshots: (a) VS + Rider side-by-side showing CGC warnings on the Naive sample, (b) Naive project's Error List panel with the 3 CGC003s. Both need to live somewhere addressable — ideally `docs/writeup/images/` committed alongside the draft.
- One human proofread pass, ideally by someone whose reaction to "callgraph closure" is *not* "yes, obviously."
- Decide the posting cadence (see `social.md` — blog first, HN + LinkedIn same day, Twitter within 24h).

**Why do it now:** every feature added before publishing muddies the clean narrative. The draft's thesis is sharpest with exactly what's already built — M3/fuzzing/etc. can come *after* the article ships and arrive as "follow-up work" rather than pre-ship scope creep.

### 2. `[MustNotThrow]` as a second-property demo

**Scope:** ~1-2 days. Spec exists at `docs/superpowers/specs/2026-04-19-must-not-throw-design.md` (written alongside this roadmap).

**Why:** the writeup's §9 ("Add your own property") shows a 50-line implementation of `[MustNotThrow]` as prose. Actually shipping it promotes that claim from "plausible" to "here's the commit, here's the test suite, here's the analyzer silent on a `TryValidate` method and loud on a `Validate` that throws." The infrastructure is done; this is the demonstration that it *generalizes* without handwaving.

**Dependency:** none. Uses the existing `CallgraphClosure.Attributes`, `CallgraphClosure.Core`, and `CallgraphClosure.ILCheck.Core` libraries unchanged. Adds a new `MustNotThrow` (Roslyn) and `MustNotThrow.ILCheck` (Cecil) project pair, each ~50 lines.

### 3. Packaging cleanup: split `MustNotAllocate.Abstractions`

**Scope:** ~2-3 hours.

**What it removes:** the absolute-path `<Analyzer Include="...">` workaround in the sample and both showcase projects. Documented in `memory/known_issues.md` as a real footgun.

**How:** extract the attribute types into a new non-`IsRoslynComponent` project (`CallgraphClosure.Attributes` already exists but needs verifying that nothing else is pulling it sideways). Update consumers to reference that project normally, and the analyzer DLL via the standard `OutputItemType="Analyzer"` pattern.

**Why medium-low priority:** the hack works; it's just ugly. Doesn't block the writeup or the next-feature work. But it's the first rough edge anyone cloning the repo will notice.

## Medium-term (next month or two)

### 4. M3: virtual dispatch + class-hierarchy analysis

**Scope:** ~1-2 weeks. Substantial — this is where the "honest limits" paragraph in the writeup currently lives.

**Why:** virtual and interface dispatch is the single biggest unsoundness in the current tool. `someInterface.Foo()` where `Foo` has twelve implementations — the analyzer only looks at the declared interface method, which has no body, so all twelve implementations are invisible.

**Two sub-designs compete here:**
- **Sound + noisy:** when we see a `callvirt`, include *all* implementations loaded in the current assembly graph. Big false-positive surface but no missed violations.
- **Precise + annotated:** adopt a DynamicallyAccessedMembers-style type-parameter annotation system and do receiver-type flow. More code, less noise, matches the trimmer's approach.

These are different milestones, not alternatives — precise is a strict improvement if you're willing to build it.

### 5. `[MustNotThrow]` showcase: exception-free error-handling pipeline

**Scope:** ~2-3 days.

**Why:** a `TryValidate(input, out error)` API surface, naive vs optimized (e.g., naive throws + catches internally; optimized is pure-return-channel), with BenchmarkDotNet numbers showing the exception-allocation-plus-unwind-cost gap. Parallel to the HTTP parser showcase but for a different predicate — proves the infrastructure really is property-agnostic and gives the writeup a natural sequel.

### 6. Differential fuzzing: Roslyn vs IL pass

**Scope:** ~1 week.

**Why:** SharpFuzz against a random-C#-source generator, compile to DLL, run both passes, diff the diagnostic lists. Any divergence is either a bug in one of them or a documented limit. This is the principled "how do you know this tool is correct?" answer, and it matches Ferrocene's article explicitly calling out the pre/post-mono delta as the interesting gap.

**Dependency on M3:** fuzzing without virtual-dispatch coverage will just rediscover the known gap repeatedly. Best done *after* M3 or with M3-style cases filtered out.

## Long-term (someday)

### 7. Upward propagation (`Direction = Upward`)

**Scope:** ~1 week in the core + property-specific demos.

**Why:** Ferrocene's original use case ("calling a validated function infects the caller") is the upward direction. The `Config.Direction` field already exists as a hook; wiring it up is a core-level change plus a demo attribute like `[ValidatedCore]`. Expands the story from "enforce what YOU don't want to do" to "enforce what you want to CONTAIN."

### 8. NativeAOT integration

**Scope:** ~2 weeks.

**Why:** reference-generic methods share IL at runtime and are invisible to post-link walks. NativeAOT specializes them. Integrating against the AOT output gives true per-instantiation coverage — the only way to catch `List<int>.Add` allocating vs `List<string>.Add` not.

**Dependency:** real understanding of the NativeAOT build pipeline and its extensibility points; this is a bigger design-space question than the other items.

### 9. ILLink plugin mode

**Scope:** ~2 weeks.

**Why:** rather than a standalone Cecil tool, hook into ILLink's own `MarkStep` / `MarkHandler` extensibility points. Gains: inherit the trimmer's handling of virtual dispatch, generics, and trim-root discovery. Loses: coupling to Microsoft's extension surface, version skew concerns. The production-grade version of the IL pass.

## Known issues (tracked in `memory/known_issues.md`)

- Analyzer + attribute ProjectReference packaging fragility (fix in item 3).
- `[MustNotAllocate]` on top-level static methods doesn't trigger the analyzer (needs investigation; may be a real Roslyn gap).
- CLI output volume was partially addressed in M2.5 via the expanded `bcl-amortized.json`. Cleanly solved for the HTTP showcase; still noisy when pointed at arbitrary third-party DLLs. Item 4 (`--stop-at-assembly`) is the principled fix.

## Decision heuristic

When choosing the next item to commit to:

- **If the writeup hasn't shipped:** pick item 1. Everything else orbits the writeup; shipping it compounds everything else.
- **If you want more stories in the article before publishing:** pick item 2. `[MustNotThrow]` is the cheapest way to demonstrate "this generalizes" concretely.
- **If you want the repo to stop embarrassing you when someone clones it cold:** pick item 3.
- **If you want the tool to be defensible under adversarial review:** pick item 4 + item 6 in that order.
- **If you have no specific constraint and want to work on something fun:** item 6 (differential fuzzing). Fuzz-finding bugs in your own tool is deeply satisfying.
