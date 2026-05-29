# Roadmap

As of 2026-05-29. Tags: `m1-complete`, `m2-complete`, `m2.5-complete`, `preview1-shipped`, `four-properties-shipped`, `mustnotthrow-showcase-complete`.

This document lists next-step options in rough priority order. Not a commitment — a menu. Each item has a scope estimate, dependencies, and the argument for/against doing it. When an item graduates into active work, write a spec for it under `docs/superpowers/specs/`, a plan under `docs/superpowers/plans/`, and update the table below with the tag.

## Where we are

| Concern | State |
|---|---|
| Roslyn analyzer for `[MustNotAllocate]` + direct-call closure | ✅ M1 |
| Cecil IL post-pass with transitive walk | ✅ M2 |
| `[AmortizedAllocation]` escape hatch + JSON annotations file | ✅ M2.5 |
| HTTP request-line parser showcase (Naive vs Optimized) with BenchmarkDotNet | ✅ M2.5 |
| `[MustNotThrow]` validation showcase (Naive throw-and-catch vs Optimized return channel) with BenchmarkDotNet | ✅ |
| `[MustNotThrow]` second-property buildout (analyzer + IL pass + tests + sample) | ✅ |
| `[MustNotBlock]` third-property buildout (analyzer + IL pass + tests + sample) | ✅ |
| `[MustNotRecurse]` fourth-property buildout with cycle detection | ✅ |
| Packaging cleanup: canonical `ProjectReference` for analyzer wiring | ✅ |
| NuGet preview1 publish (7 packages under `CallgraphClosure.*`) | ✅ `0.1.0-preview1` |
| `0.1.0-preview2` repack with local-function fix + top-level README | ✅ pushed to nuget.org |
| Local-function analyzer coverage (M1 issue #2 — top-level static methods) | ✅ |
| `.sln` → `.slnx` solution-file migration | ✅ |
| Writeup long-form draft | ✅ `docs/writeup/draft.md` (4600 words) |
| Social variants (Twitter / LinkedIn / HN) | ✅ `docs/writeup/social.md` |
| ILLink class-name + Ferrocene URL fact-checks | ✅ |
| Repo URL fill-in, IDE screenshots, human proofread | ⏳ remaining writeup polish |

## Medium-term (next month or two)

### 1. M3: virtual dispatch + class-hierarchy analysis

**Scope:** ~1-2 weeks. Substantial — this is where the "honest limits" paragraph in the writeup currently lives.

**Why:** virtual and interface dispatch is the single biggest unsoundness in the current tool. `someInterface.Foo()` where `Foo` has twelve implementations — the analyzer only looks at the declared interface method, which has no body, so all twelve implementations are invisible.

**Two sub-designs compete here:**
- **Sound + noisy:** when we see a `callvirt`, include *all* implementations loaded in the current assembly graph. Big false-positive surface but no missed violations.
- **Precise + annotated:** adopt a DynamicallyAccessedMembers-style type-parameter annotation system and do receiver-type flow. More code, less noise, matches the trimmer's approach.

These are different milestones, not alternatives — precise is a strict improvement if you're willing to build it.

### 2. `[MustNotThrow]` showcase: exception-free error-handling pipeline

**Status:** ✅ Done (2026-05-29). Implemented as the quantity-validation showcase: `src/Showcase.Validation.{Common,Naive,Optimized}` + `bench/Showcase.Validation.Benchmarks`. Naive throws-and-catches internally (flagged by `[MustNotThrow]` through the try/catch); Optimized is a pure span-based return channel (zero diagnostics). BDN out-of-range path: naive 1,151 ns / 232 B vs optimized 2.55 ns / 0 B. See `docs/superpowers/specs/2026-05-28-mustnotthrow-showcase-design.md` and `docs/superpowers/plans/2026-05-29-mustnotthrow-showcase.md`.

**Scope:** ~2-3 days.

**Why:** a `TryValidate(input, out error)` API surface, naive vs optimized (e.g., naive throws + catches internally; optimized is pure-return-channel), with BenchmarkDotNet numbers showing the exception-allocation-plus-unwind-cost gap. Parallel to the HTTP parser showcase but for a different predicate — proves the infrastructure really is property-agnostic and gives the writeup a natural sequel.

### 3. Differential fuzzing: Roslyn vs IL pass

**Scope:** ~1 week.

**Why:** SharpFuzz against a random-C#-source generator, compile to DLL, run both passes, diff the diagnostic lists. Any divergence is either a bug in one of them or a documented limit. This is the principled "how do you know this tool is correct?" answer, and it matches Ferrocene's article explicitly calling out the pre/post-mono delta as the interesting gap.

**Dependency on M3:** fuzzing without virtual-dispatch coverage will just rediscover the known gap repeatedly. Best done *after* M3 or with M3-style cases filtered out.

## Long-term (someday)

### 4. Upward propagation (`Direction = Upward`)

**Scope:** ~1 week in the core + property-specific demos.

**Why:** Ferrocene's original use case ("calling a validated function infects the caller") is the upward direction. The `Config.Direction` field already exists as a hook; wiring it up is a core-level change plus a demo attribute like `[ValidatedCore]`. Expands the story from "enforce what YOU don't want to do" to "enforce what you want to CONTAIN."

### 5. NativeAOT integration

**Scope:** ~2 weeks.

**Why:** reference-generic methods share IL at runtime and are invisible to post-link walks. NativeAOT specializes them. Integrating against the AOT output gives true per-instantiation coverage — the only way to catch `List<int>.Add` allocating vs `List<string>.Add` not.

**Dependency:** real understanding of the NativeAOT build pipeline and its extensibility points; this is a bigger design-space question than the other items.

### 6. ILLink plugin mode

**Scope:** ~2 weeks.

**Why:** rather than a standalone Cecil tool, hook into ILLink's own `MarkStep` / `MarkHandler` extensibility points. Gains: inherit the trimmer's handling of virtual dispatch, generics, and trim-root discovery. Loses: coupling to Microsoft's extension surface, version skew concerns. The production-grade version of the IL pass.

### 7. Ship the writeup

**Scope:** ~2-4 hours of polish + publishing ceremony. Deferred — picked up when the author is ready, not on a milestone clock.

**What's left before posting:**
- Fill the `<TBD>` repo URL placeholder in both `draft.md` and `social.md` (now resolved: `github.com/pawlos/covenant`).
- Two IDE screenshots: (a) VS + Rider side-by-side showing CGC warnings on the Naive sample, (b) Naive project's Error List panel with the 3 CGC003s. Both need to live somewhere addressable — ideally `docs/writeup/images/` committed alongside the draft.
- One human proofread pass, ideally by someone whose reaction to "callgraph closure" is *not* "yes, obviously."
- Decide the posting cadence (see `social.md` — blog first, HN + LinkedIn same day, Twitter within 24h).

**Why deferred:** the original argument for shipping early was that pre-ship features muddy the narrative. That trade is now explicitly reversed — features added before publishing strengthen the article (extra properties, packaging polish, more showcases all become "look how this generalizes" rather than "scope creep"). Publish when the story feels complete.

## Known issues (tracked in `memory/known_issues.md`)

- CLI output volume was partially addressed in M2.5 via the expanded `bcl-amortized.json`. Cleanly solved for the HTTP showcase; still noisy when pointed at arbitrary third-party DLLs. Item 1 (`--stop-at-assembly` is one principled fix; full M3 dispatch handling is the other) reduces this further.
- Roslyn version skew between the test framework (pinned `Microsoft.CodeAnalysis.CSharp` 4.8.0) and the .NET 10 preview SDK's shipped Roslyn: `throw new X()` exposes its inner ctor as a walked operation under the pinned version (firing CGC002), but not under the shipped one (only CGC003 fires in real builds). The MustNotThrow test suite asserts both; production behavior is CGC003-only. Bump the pin and update assertions, or document as a known limit.

## Decision heuristic

When choosing the next item to commit to:

- **If you want the tool to be defensible under adversarial review:** pick item 1 (M3 dispatch) + item 3 (differential fuzzing) in that order. Item 1 unblocks item 3.
- **If you want the BDN-backed sequel story for the writeup:** item 2 is now done (the validation showcase) — the artifacts exist; what remains is folding them into a writeup sequel section (part of item 7).
- **If you have no specific constraint and want to work on something fun:** item 3 (differential fuzzing). Best done after item 1; doing it before just rediscovers the dispatch gap.
- **If the writeup feels ready to ship:** pick item 7. The pre-ship gating list is small and known.
