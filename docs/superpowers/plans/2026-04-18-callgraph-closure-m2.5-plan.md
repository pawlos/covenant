# Callgraph-Closure Lint M2.5 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `[AmortizedAllocation]` as a walk-terminator attribute + external JSON annotations file, ship an HTTP request-line parser showcase (Naive vs Optimized) with `.expected.txt` analyzer outputs committed, and produce BenchmarkDotNet numbers showing the allocation + throughput gap.

**Architecture:** New shared `CallgraphClosure.Attributes` library hosts both `[MustNotAllocate]` (moved from M1) and `[AmortizedAllocation]` (new). Both analyzers (Roslyn + IL) get one new `continue` branch for amortized callees. Annotations file loaded via `<AdditionalFiles>` (Roslyn) or `--amortized-file` CLI flag (IL). Showcase is three small projects under `src/Showcase.Http.*` plus a BenchmarkDotNet project.

**Tech Stack:** Same as M1+M2 (Roslyn 4.8.0, Mono.Cecil 0.11.5, xunit 2.4.2 pinned, net10.0 for everything except analyzers on netstandard2.0) plus `BenchmarkDotNet` 0.13.12 for benchmarks, `System.Text.Json` (BCL) for JSON annotations parsing.

**Reference spec:** `docs/superpowers/specs/2026-04-18-callgraph-closure-m2.5-design.md`

**Known short-term divergence worth naming up front:** the Roslyn analyzer (Task 7) consumes JSON annotations in Roslyn-display FQN form (`"ContainingType.MethodName(ParamType, ...)"`, language keywords for primitives). The IL CLI (Task 8) consumes them in Cecil FQN form (`"ReturnType DeclaringType::MethodName(ParamTypes)"`, full BCL names). This means there are effectively **two JSON files with different content conventions** — one per analyzer — even though both describe "amortized methods." Unifying the format is out of scope for M2.5; tracked for a future cleanup. When adjusting either file, check the consumer.

---

## Task 1: Create CallgraphClosure.Attributes project

Extract `MustNotAllocateAttribute` out of the M1 analyzer project into its own library. Add `AmortizedAllocationAttribute` alongside. Both end up in namespace `CallgraphClosure.Attributes`.

**Files:**
- Create: `src/CallgraphClosure.Attributes/CallgraphClosure.Attributes.csproj`
- Create: `src/CallgraphClosure.Attributes/MustNotAllocateAttribute.cs`
- Create: `src/CallgraphClosure.Attributes/AmortizedAllocationAttribute.cs`
- Delete: `src/MustNotAllocate/MustNotAllocateAttribute.cs`

- [ ] **Step 1: Create the project**

Run:
```bash
dotnet new classlib -o src/CallgraphClosure.Attributes -f netstandard2.0
rm src/CallgraphClosure.Attributes/Class1.cs
dotnet sln add src/CallgraphClosure.Attributes/CallgraphClosure.Attributes.csproj
```

- [ ] **Step 2: Configure the .csproj**

Overwrite `src/CallgraphClosure.Attributes/CallgraphClosure.Attributes.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
  </PropertyGroup>
</Project>
```

No package references — just attribute types.

- [ ] **Step 3: Write MustNotAllocateAttribute.cs**

Create `src/CallgraphClosure.Attributes/MustNotAllocateAttribute.cs`:

```csharp
using System;

namespace CallgraphClosure.Attributes;

[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Constructor,
    AllowMultiple = false,
    Inherited = false)]
public sealed class MustNotAllocateAttribute : Attribute { }
```

- [ ] **Step 4: Write AmortizedAllocationAttribute.cs**

Create `src/CallgraphClosure.Attributes/AmortizedAllocationAttribute.cs`:

```csharp
using System;

namespace CallgraphClosure.Attributes;

[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Constructor,
    AllowMultiple = false,
    Inherited = false)]
public sealed class AmortizedAllocationAttribute : Attribute { }
```

- [ ] **Step 5: Delete the old attribute file**

Run: `rm src/MustNotAllocate/MustNotAllocateAttribute.cs`

- [ ] **Step 6: Update MustNotAllocate.csproj to reference Attributes**

In `src/MustNotAllocate/MustNotAllocate.csproj`, after the `<PackageReference Include="Microsoft.CodeAnalysis.CSharp" ... />` line, add:

```xml
    <ProjectReference Include="..\CallgraphClosure.Attributes\CallgraphClosure.Attributes.csproj" />
```

Full expected ItemGroup after the change:

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" PrivateAssets="all" />
    <ProjectReference Include="..\CallgraphClosure.Core\CallgraphClosure.Core.csproj" />
    <ProjectReference Include="..\CallgraphClosure.Attributes\CallgraphClosure.Attributes.csproj" />
  </ItemGroup>
```

- [ ] **Step 7: Verify build**

Run: `dotnet build src/CallgraphClosure.Attributes/ src/MustNotAllocate/`
Expected: both projects build 0/0.

Note: M1 tests will now fail because fixture sources use `using MustNotAllocate;`. That's fixed in Task 3. Don't run tests yet.

- [ ] **Step 8: Commit**

```bash
git add src/CallgraphClosure.Attributes/
git add src/MustNotAllocate/MustNotAllocate.csproj
git rm src/MustNotAllocate/MustNotAllocateAttribute.cs
git commit -m "refactor: extract attributes into CallgraphClosure.Attributes project"
```

---

## Task 2: Update analyzer FQN strings

The two analyzers (M1 Roslyn + M2 IL) look up attributes by fully-qualified metadata name. Update those strings.

**Files:**
- Modify: `src/MustNotAllocate/MustNotAllocateAnalyzer.cs`
- Modify: `src/MustNotAllocate.ILCheck/MustNotAllocateIlAnalyzer.cs`

- [ ] **Step 1: Update Roslyn analyzer FQN**

In `src/MustNotAllocate/MustNotAllocateAnalyzer.cs`, change:

```csharp
AttributeFullName: "MustNotAllocate.MustNotAllocateAttribute",
```

to:

```csharp
AttributeFullName: "CallgraphClosure.Attributes.MustNotAllocateAttribute",
```

- [ ] **Step 2: Update IL analyzer FQN**

In `src/MustNotAllocate.ILCheck/MustNotAllocateIlAnalyzer.cs`, change:

```csharp
public const string AttributeFullName = "MustNotAllocate.MustNotAllocateAttribute";
```

to:

```csharp
public const string AttributeFullName = "CallgraphClosure.Attributes.MustNotAllocateAttribute";
```

- [ ] **Step 3: Build to verify no compile errors introduced**

Run: `dotnet build src/MustNotAllocate/ src/MustNotAllocate.ILCheck/`
Expected: both build 0/0. (Tests still fail — fixed in Task 3.)

- [ ] **Step 4: Commit**

```bash
git add src/MustNotAllocate/MustNotAllocateAnalyzer.cs
git add src/MustNotAllocate.ILCheck/MustNotAllocateIlAnalyzer.cs
git commit -m "refactor: update analyzer FQN strings to CallgraphClosure.Attributes namespace"
```

---

## Task 3: Migrate consumers to new namespace

Update all files that reference `using MustNotAllocate;` or `typeof(MustNotAllocate.MustNotAllocateAttribute)` to use the new namespace. This is where the M1/M2 test suite will either stay green or reveal we missed something.

**Files (exhaustive list — verify by grep before starting):**
- Modify: `src/MustNotAllocate.Sample/Program.cs`
- Modify: `tests/MustNotAllocate.Tests/CSharpAnalyzerVerifier.cs`
- Modify: `tests/MustNotAllocate.ILCheck.Tests/CompileFixture.cs`
- Modify: Every test file in `tests/MustNotAllocate.Tests/` and `tests/MustNotAllocate.ILCheck.Tests/` that has a C# fixture source string containing `using MustNotAllocate;`

- [ ] **Step 1: Find all affected files**

Run:
```bash
grep -rln "using MustNotAllocate" tests/ src/ || true
grep -rln "typeof(MustNotAllocate.MustNotAllocateAttribute)\|typeof(global::MustNotAllocate.MustNotAllocateAttribute)" tests/ src/ || true
```

Expected: a list of ~15-20 files. Every fixture source string that does `using MustNotAllocate;` needs to become `using CallgraphClosure.Attributes;`. Every `typeof` reference needs the namespace updated.

**Note on fixture sources:** they're inside C# raw string literals in test files. Changes happen to string content, not to the surrounding C# code. Be careful not to change the literals' indentation or trailing whitespace — raw strings preserve it.

- [ ] **Step 2: Update the sample program**

In `src/MustNotAllocate.Sample/Program.cs`, change the top `using MustNotAllocate;` directive to:

```csharp
using CallgraphClosure.Attributes;
```

The `[MustNotAllocate]` usages in the file stay — only the using directive changes.

- [ ] **Step 3: Update the M1 test verifier**

In `tests/MustNotAllocate.Tests/CSharpAnalyzerVerifier.cs`, change:

```csharp
typeof(MustNotAllocateAttribute).Assembly.Location
```

to:

```csharp
typeof(CallgraphClosure.Attributes.MustNotAllocateAttribute).Assembly.Location
```

- [ ] **Step 4: Update the M2 compile fixture**

In `tests/MustNotAllocate.ILCheck.Tests/CompileFixture.cs`, change **both** references from:

```csharp
typeof(global::MustNotAllocate.MustNotAllocateAttribute)
```

to:

```csharp
typeof(global::CallgraphClosure.Attributes.MustNotAllocateAttribute)
```

- [ ] **Step 5: Update all fixture source strings**

In every test file under `tests/MustNotAllocate.Tests/` and `tests/MustNotAllocate.ILCheck.Tests/` that contains a fixture source string, change the `using MustNotAllocate;` line inside the string to `using CallgraphClosure.Attributes;`.

**Approach:** use `sed -i 's|using MustNotAllocate;|using CallgraphClosure.Attributes;|g' <files>` on the test files. This is a mechanical find/replace; no semantic changes.

After replacement, visually spot-check one fixture in each test file to confirm no accidental changes elsewhere.

- [ ] **Step 6: Update the M1 NoAttributeReferenceTests fixture**

`tests/MustNotAllocate.Tests/NoAttributeReferenceTests.cs` has a fixture that defines `namespace Other { class MustNotAllocateAttribute : System.Attribute { } }` — testing that same-named attributes in different namespaces don't match. That fixture is UNCHANGED (it never used the real `using MustNotAllocate;`).

But verify by inspection that the FQN-match test still makes sense: the analyzer now looks for `CallgraphClosure.Attributes.MustNotAllocateAttribute`, and `Other.MustNotAllocateAttribute` is still a different FQN. Test still valid.

- [ ] **Step 7: Run the full suite**

Run: `dotnet test`
Expected: 31 tests pass (M1's 18 + M2's 13).

If any test fails with "[MustNotAllocate] not found" or similar resolution error, a fixture string wasn't updated. Find the offending file via the failure message and fix.

- [ ] **Step 8: Verify the sample still emits the 2 warnings**

Run: `dotnet build CallgraphClosure.sln 2>&1 | grep CGC`
Expected: `CGC002` on `Console.WriteLine` + `CGC003` on `new int[16]`.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "refactor: migrate consumers to CallgraphClosure.Attributes namespace"
```

---

## Task 4: Add [AmortizedAllocation] walk-terminator to Roslyn analyzer (TDD)

Extend `CallgraphClosureAnalyzer` in the M1 Roslyn core to accept a second attribute FQN for the "amortized" concept. Walker treats calls to amortized-marked methods the same as calls to propagating-attribute-marked methods: skip (continue).

**Files:**
- Modify: `src/CallgraphClosure.Core/Config.cs` (add `AmortizedAttributeFullName` field)
- Modify: `src/CallgraphClosure.Core/CallgraphClosureAnalyzer.cs` (resolve the new attr, check in VisitOp)
- Modify: `src/MustNotAllocate/MustNotAllocateAnalyzer.cs` (pass the new FQN)
- Create: `tests/MustNotAllocate.Tests/AmortizedAllocationTests.cs`

- [ ] **Step 1: Write failing test**

Create `tests/MustNotAllocate.Tests/AmortizedAllocationTests.cs`:

```csharp
using System.Threading.Tasks;
using CallgraphClosure.Core;
using Xunit;

namespace MustNotAllocate.Tests;

public class AmortizedAllocationTests
{
    [Fact]
    public async Task AnnotatedCaller_CallsAmortizedMethod_FiresNothing()
    {
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotAllocate]
                void Caller() { Rent(); }

                [AmortizedAllocation]
                byte[] Rent() => new byte[4096];
            }
            """;

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task AnnotatedCaller_CallsUnannotatedMethod_StillFiresCGC001()
    {
        // Regression: non-amortized unannotated callees still produce CGC001.
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotAllocate]
                void Caller() { NotPooled(); }

                byte[] NotPooled() => new byte[4096];
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SourceBoundary)
            .WithLocation(6, 21)
            .WithArguments("Caller", "MustNotAllocateAttribute", "NotPooled");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }
}
```

- [ ] **Step 2: Run — first fails, second passes**

Run: `dotnet test tests/MustNotAllocate.Tests/ --filter "FullyQualifiedName~AmortizedAllocation"`
Expected: first test FAILS (`Rent` call still fires CGC001 because the analyzer doesn't know about `[AmortizedAllocation]` yet); second passes.

- [ ] **Step 3: Extend Config to carry the amortized attribute FQN**

Overwrite `src/CallgraphClosure.Core/Config.cs`:

```csharp
using System.Collections.Immutable;

namespace CallgraphClosure.Core;

public sealed record Config(
    string AttributeFullName,
    PropagationDirection Direction,
    ImmutableArray<ISink> Sinks,
    string? AmortizedAttributeFullName = null);
```

`AmortizedAttributeFullName` is nullable — a concrete analyzer that doesn't care about amortization can omit it (keeps backward compat).

- [ ] **Step 4: Extend the Roslyn analyzer to consult amortized attr**

In `src/CallgraphClosure.Core/CallgraphClosureAnalyzer.cs`, update `OnStart` and `VisitOp` to resolve and check the amortized attribute. Replace the whole file:

```csharp
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CallgraphClosure.Core;

public abstract class CallgraphClosureAnalyzer : DiagnosticAnalyzer
{
    private readonly Config _config;

    protected CallgraphClosureAnalyzer(Config config) => _config = config;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            Diagnostics.SourceBoundary,
            Diagnostics.ExternalBoundary,
            Diagnostics.SinkHit);

    public override void Initialize(AnalysisContext ctx)
    {
        ctx.EnableConcurrentExecution();
        ctx.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        ctx.RegisterCompilationStartAction(OnStart);
    }

    private void OnStart(CompilationStartAnalysisContext c)
    {
        var attrSym = c.Compilation.GetTypeByMetadataName(_config.AttributeFullName);
        if (attrSym is null) return;

        var amortizedSym = _config.AmortizedAttributeFullName is null
            ? null
            : c.Compilation.GetTypeByMetadataName(_config.AmortizedAttributeFullName);

        c.RegisterOperationBlockAction(b => AnalyzeBlock(b, attrSym, amortizedSym, c.Compilation));
    }

    private void AnalyzeBlock(
        OperationBlockAnalysisContext b,
        INamedTypeSymbol attrSym,
        INamedTypeSymbol? amortizedSym,
        Compilation compilation)
    {
        if (b.OwningSymbol is not IMethodSymbol caller) return;
        if (!HasAttribute(caller, attrSym)) return;

        foreach (var block in b.OperationBlocks)
        {
            foreach (var op in block.DescendantsAndSelf())
            {
                VisitOp(op, caller, attrSym, amortizedSym, compilation, b);
            }
        }
    }

    private void VisitOp(
        IOperation op,
        IMethodSymbol caller,
        INamedTypeSymbol attrSym,
        INamedTypeSymbol? amortizedSym,
        Compilation compilation,
        OperationBlockAnalysisContext b)
    {
        // Skip object creations that are attribute applications — those are not
        // runtime allocations in the annotated method body.
        if (op is IObjectCreationOperation && op.Parent is IAttributeOperation) return;

        foreach (var sink in _config.Sinks)
        {
            var label = sink.Match(op);
            if (label is null) continue;

            b.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.SinkHit,
                op.Syntax.GetLocation(),
                caller.Name,
                attrSym.Name,
                label));
        }

        IMethodSymbol? target = op switch
        {
            IInvocationOperation inv => inv.TargetMethod,
            IObjectCreationOperation oc => oc.Constructor,
            _ => null,
        };

        if (target is null) return;

        var original = target.OriginalDefinition;
        if (HasAttribute(original, attrSym)) return;
        if (amortizedSym is not null && HasAttribute(original, amortizedSym)) return;

        var isExternal = !SymbolEqualityComparer.Default.Equals(
            original.ContainingAssembly, compilation.Assembly);

        var descriptor = isExternal
            ? Diagnostics.ExternalBoundary
            : Diagnostics.SourceBoundary;

        var targetName = op is IObjectCreationOperation
            ? original.ContainingType.Name
            : original.Name;

        b.ReportDiagnostic(Diagnostic.Create(
            descriptor,
            op.Syntax.GetLocation(),
            caller.Name,
            attrSym.Name,
            targetName));
    }

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attrSym) =>
        symbol.GetAttributes().Any(a =>
            SymbolEqualityComparer.Default.Equals(a.AttributeClass, attrSym));
}
```

The new line is `if (amortizedSym is not null && HasAttribute(original, amortizedSym)) return;` right after the existing same-shape check for `attrSym`.

- [ ] **Step 5: Wire the amortized FQN into MustNotAllocateAnalyzer**

In `src/MustNotAllocate/MustNotAllocateAnalyzer.cs`, update the Config constructor call:

```csharp
public MustNotAllocateAnalyzer() : base(new Config(
    AttributeFullName: "CallgraphClosure.Attributes.MustNotAllocateAttribute",
    Direction: PropagationDirection.Downward,
    Sinks: ImmutableArray.Create<ISink>(
        new ObjectCreationSink(),
        new ArrayCreationSink(),
        new BoxingConversionSink()),
    AmortizedAttributeFullName: "CallgraphClosure.Attributes.AmortizedAllocationAttribute")) { }
```

- [ ] **Step 6: Run the AmortizedAllocation tests**

Run: `dotnet test tests/MustNotAllocate.Tests/ --filter "FullyQualifiedName~AmortizedAllocation"`
Expected: both tests pass.

- [ ] **Step 7: Run full suite**

Run: `dotnet test`
Expected: 33 tests pass (31 previous + 2 new).

- [ ] **Step 8: Commit**

```bash
git add src/CallgraphClosure.Core/Config.cs
git add src/CallgraphClosure.Core/CallgraphClosureAnalyzer.cs
git add src/MustNotAllocate/MustNotAllocateAnalyzer.cs
git add tests/MustNotAllocate.Tests/AmortizedAllocationTests.cs
git commit -m "feat(roslyn): recognize [AmortizedAllocation] as walk-terminator"
```

---

## Task 5: Add [AmortizedAllocation] walk-terminator to IL walker (TDD)

Same extension on the M2 Cecil side. `ClosureWalker` learns a second attribute FQN that terminates the walk.

**Files:**
- Modify: `src/CallgraphClosure.ILCheck.Core/ClosureWalker.cs`
- Modify: `src/MustNotAllocate.ILCheck/MustNotAllocateIlAnalyzer.cs`
- Create: `tests/MustNotAllocate.ILCheck.Tests/AmortizedAllocationTests.cs`

- [ ] **Step 1: Write failing test**

Create `tests/MustNotAllocate.ILCheck.Tests/AmortizedAllocationTests.cs`:

```csharp
using System.Collections.Immutable;
using System.Linq;
using CallgraphClosure.ILCheck.Core;
using MustNotAllocate.ILCheck;
using Mono.Cecil;
using Xunit;

namespace MustNotAllocate.ILCheck.Tests;

public class AmortizedAllocationTests
{
    private static ClosureWalker BuildWalker() => new(
        MustNotAllocateIlAnalyzer.AttributeFullName,
        MustNotAllocateIlAnalyzer.Sinks,
        propertyName: "MustNotAllocate",
        amortizedAttributeFullName: MustNotAllocateIlAnalyzer.AmortizedAttributeFullName);

    [Fact]
    public void AnnotatedCaller_CallsAmortizedHelper_FiresNothing()
    {
        var source = """
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotAllocate]
                public void Caller() { Rent(); }

                [AmortizedAllocation]
                public byte[] Rent() => new byte[4096];
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        Assert.Empty(diagnostics);
    }
}
```

Note: the test references a new constructor parameter `amortizedAttributeFullName` on `ClosureWalker`, and a new `AmortizedAttributeFullName` constant on `MustNotAllocateIlAnalyzer`. Both are added below.

- [ ] **Step 2: Run — expect compile failure first**

Run: `dotnet build tests/MustNotAllocate.ILCheck.Tests/`
Expected: compilation errors — the constructor signature and the constant don't exist yet. That's the "red" state; we're going to fix it in Steps 3-5.

- [ ] **Step 3: Add the amortized FQN constant to the analyzer binding**

In `src/MustNotAllocate.ILCheck/MustNotAllocateIlAnalyzer.cs`, inside the static class, add:

```csharp
public const string AmortizedAttributeFullName = "CallgraphClosure.Attributes.AmortizedAllocationAttribute";
```

Full expected file content after the change:

```csharp
using System.Collections.Immutable;
using CallgraphClosure.ILCheck.Core;
using MustNotAllocate.ILCheck.Sinks;

namespace MustNotAllocate.ILCheck;

public static class MustNotAllocateIlAnalyzer
{
    public const string AttributeFullName = "CallgraphClosure.Attributes.MustNotAllocateAttribute";

    public const string AmortizedAttributeFullName = "CallgraphClosure.Attributes.AmortizedAllocationAttribute";

    public static ImmutableArray<IIlSink> Sinks { get; } =
        ImmutableArray.Create<IIlSink>(
            new NewObjSink(),
            new NewArrSink(),
            new BoxSink());
}
```

- [ ] **Step 4: Extend ClosureWalker with amortized handling**

Overwrite `src/CallgraphClosure.ILCheck.Core/ClosureWalker.cs`:

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace CallgraphClosure.ILCheck.Core;

public sealed class ClosureWalker
{
    private readonly string _attributeFullName;
    private readonly ImmutableArray<IIlSink> _sinks;
    private readonly string _propertyName;
    private readonly string? _amortizedAttributeFullName;

    public ClosureWalker(
        string attributeFullName,
        ImmutableArray<IIlSink> sinks,
        string propertyName,
        string? amortizedAttributeFullName = null)
    {
        _attributeFullName = attributeFullName;
        _sinks = sinks;
        _propertyName = propertyName;
        _amortizedAttributeFullName = amortizedAttributeFullName;
    }

    public ImmutableArray<Diagnostic> Analyze(AssemblyDefinition assembly)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (var type in assembly.MainModule.Types)
        {
            foreach (var method in type.Methods)
            {
                if (!HasAttributeByFullName(method, _attributeFullName)) continue;

                var visited = new HashSet<string>();
                VisitMethodBody(
                    method,
                    annotatedCaller: method,
                    chain: ImmutableArray.Create<MethodReference>(method),
                    visited,
                    diagnostics);
            }
        }

        return diagnostics.ToImmutable();
    }

    private void VisitMethodBody(
        MethodDefinition method,
        MethodDefinition annotatedCaller,
        ImmutableArray<MethodReference> chain,
        HashSet<string> visited,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (method.Body is null) return;
        if (!visited.Add(method.FullName)) return;

        foreach (var instruction in method.Body.Instructions)
        {
            foreach (var sink in _sinks)
            {
                var label = sink.Match(instruction);
                if (label is null) continue;

                diagnostics.Add(new Diagnostic(
                    Id: DiagnosticIds.SinkHit,
                    PropertyName: _propertyName,
                    AnnotatedCaller: annotatedCaller,
                    Chain: chain,
                    SinkLabel: label,
                    UnresolvedTarget: null));
            }

            var target = ExtractCallTarget(instruction);
            if (target is null) continue;

            MethodDefinition? resolved;
            try
            {
                resolved = target.Resolve();
            }
            catch
            {
                resolved = null;
            }

            if (resolved is not null && HasAttributeByFullName(resolved, _attributeFullName))
                continue;

            if (resolved is not null &&
                _amortizedAttributeFullName is not null &&
                HasAttributeByFullName(resolved, _amortizedAttributeFullName))
                continue;

            if (resolved?.Body is not null)
            {
                VisitMethodBody(
                    resolved,
                    annotatedCaller,
                    chain.Add(target),
                    visited,
                    diagnostics);
                continue;
            }

            var sameAssembly =
                resolved is not null &&
                resolved.Module.Assembly == annotatedCaller.Module.Assembly;

            diagnostics.Add(new Diagnostic(
                Id: sameAssembly ? DiagnosticIds.SourceBoundary : DiagnosticIds.ExternalBoundary,
                PropertyName: _propertyName,
                AnnotatedCaller: annotatedCaller,
                Chain: chain.Add(target),
                SinkLabel: null,
                UnresolvedTarget: resolved is null ? target : null));
        }
    }

    private static MethodReference? ExtractCallTarget(Instruction instruction)
    {
        if (instruction.OpCode == OpCodes.Call ||
            instruction.OpCode == OpCodes.Callvirt ||
            instruction.OpCode == OpCodes.Newobj)
        {
            return instruction.Operand as MethodReference;
        }
        return null;
    }

    private static bool HasAttributeByFullName(MethodDefinition method, string attributeFullName)
    {
        foreach (var attr in method.CustomAttributes)
        {
            if (attr.AttributeType.FullName == attributeFullName)
                return true;
        }
        return false;
    }
}
```

The changes from the M2 version:
- Add `_amortizedAttributeFullName` field + optional constructor parameter
- Rename `HasPropagatingAttribute` → `HasAttributeByFullName` (now takes the attribute name as a parameter so both attributes can use it)
- Add one new `continue` branch for amortized-marked callees

- [ ] **Step 5: Rebuild, run the new test**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/ --filter "FullyQualifiedName~AmortizedAllocation"`
Expected: the test passes.

- [ ] **Step 6: Run full suite**

Run: `dotnet test`
Expected: 34 tests pass (M1: 18 + 2 = 20, M2: 13 + 1 = 14).

- [ ] **Step 7: Verify the existing sample still has the same 2 diagnostics at M2 CLI level**

Run:
```bash
dotnet build src/MustNotAllocate.Sample/
dotnet run --project src/CallgraphClosure.ILCheck.Cli/ -- \
    src/MustNotAllocate.Sample/bin/Debug/net10.0/MustNotAllocate.Sample.dll 2>&1 | grep "array allocation" | head -5
```

Expected: the direct `new int[16]` in `Tick` still fires CGC003. (The sample doesn't use `[AmortizedAllocation]`, so M2.5 doesn't change its output.)

- [ ] **Step 8: Commit**

```bash
git add src/CallgraphClosure.ILCheck.Core/ClosureWalker.cs
git add src/MustNotAllocate.ILCheck/MustNotAllocateIlAnalyzer.cs
git add tests/MustNotAllocate.ILCheck.Tests/AmortizedAllocationTests.cs
git commit -m "feat(ilcheck): recognize [AmortizedAllocation] as walk-terminator"
```

---

## Task 6: JSON annotations file parser (shared utility)

Add a shared `AmortizedSet` type in `CallgraphClosure.ILCheck.Core` that parses JSON and exposes a `Contains(string methodFqn)` check. This task adds the core data type + its unit tests only — wiring into the analyzers happens in Tasks 7 and 8.

**Files:**
- Create: `src/CallgraphClosure.ILCheck.Core/AmortizedSet.cs`
- Create: `tests/MustNotAllocate.ILCheck.Tests/AmortizedSetTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/MustNotAllocate.ILCheck.Tests/AmortizedSetTests.cs`:

```csharp
using CallgraphClosure.ILCheck.Core;
using Xunit;

namespace MustNotAllocate.ILCheck.Tests;

public class AmortizedSetTests
{
    [Fact]
    public void Parse_ValidJson_ReturnsSetWithEntries()
    {
        var json = """
            {
              "amortized_methods": [
                "System.Buffers.ArrayPool`1.Rent(Int32)",
                "System.Buffers.MemoryPool`1.Rent(Int32)"
              ]
            }
            """;

        var set = AmortizedSet.Parse(json);

        Assert.True(set.Contains("System.Buffers.ArrayPool`1.Rent(Int32)"));
        Assert.True(set.Contains("System.Buffers.MemoryPool`1.Rent(Int32)"));
        Assert.False(set.Contains("Something.Else.Method()"));
    }

    [Fact]
    public void Parse_EmptyArray_ReturnsEmptySet()
    {
        var set = AmortizedSet.Parse("""{"amortized_methods": []}""");
        Assert.False(set.Contains("anything"));
    }

    [Fact]
    public void Parse_MalformedJson_ThrowsFormatException()
    {
        Assert.Throws<System.FormatException>(
            () => AmortizedSet.Parse("not json at all"));
    }

    [Fact]
    public void Parse_MissingKey_ReturnsEmptySet()
    {
        // Valid JSON but no amortized_methods key — treat as empty, don't throw.
        var set = AmortizedSet.Parse("""{"other": "stuff"}""");
        Assert.False(set.Contains("anything"));
    }

    [Fact]
    public void Empty_IsAlwaysDefinedAndContainsNothing()
    {
        Assert.False(AmortizedSet.Empty.Contains("anything"));
    }
}
```

- [ ] **Step 2: Run — expect all to fail (type doesn't exist)**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/ --filter "FullyQualifiedName~AmortizedSetTests"`
Expected: compilation error — `AmortizedSet` doesn't exist yet.

- [ ] **Step 3: Implement AmortizedSet**

Create `src/CallgraphClosure.ILCheck.Core/AmortizedSet.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;

namespace CallgraphClosure.ILCheck.Core;

public sealed class AmortizedSet
{
    private readonly ImmutableHashSet<string> _methods;

    private AmortizedSet(ImmutableHashSet<string> methods) => _methods = methods;

    public static AmortizedSet Empty { get; } = new(ImmutableHashSet<string>.Empty);

    public bool Contains(string methodFullName) => _methods.Contains(methodFullName);

    public static AmortizedSet Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException("Amortized annotations file is not valid JSON", ex);
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("amortized_methods", out var arr))
                return Empty;

            if (arr.ValueKind != JsonValueKind.Array)
                throw new FormatException("'amortized_methods' must be a JSON array");

            var builder = ImmutableHashSet.CreateBuilder<string>();
            foreach (var element in arr.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                    throw new FormatException("'amortized_methods' entries must be strings");

                var name = element.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                    builder.Add(name);
            }

            return new AmortizedSet(builder.ToImmutable());
        }
    }
}
```

- [ ] **Step 4: Run the tests — all pass**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/ --filter "FullyQualifiedName~AmortizedSetTests"`
Expected: 5 tests pass.

- [ ] **Step 5: Run full suite — 39 tests**

Run: `dotnet test`
Expected: 39 tests pass (34 + 5).

- [ ] **Step 6: Commit**

```bash
git add src/CallgraphClosure.ILCheck.Core/AmortizedSet.cs
git add tests/MustNotAllocate.ILCheck.Tests/AmortizedSetTests.cs
git commit -m "feat(ilcheck): add AmortizedSet JSON parser"
```

---

## Task 7: Roslyn analyzer reads annotations file (TDD)

Hook the Roslyn analyzer into the AdditionalFiles stream. Parse once per compilation start; inject the resulting set into the walk as a supplementary check alongside `_amortizedAttributeFullName`.

**Files:**
- Modify: `src/CallgraphClosure.Core/Config.cs` (add `AmortizedFileName` convention)
- Modify: `src/CallgraphClosure.Core/CallgraphClosureAnalyzer.cs` (read AdditionalFiles, plug into VisitOp)
- Create: `tests/MustNotAllocate.Tests/AmortizedAnnotationsFileTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/MustNotAllocate.Tests/AmortizedAnnotationsFileTests.cs`:

```csharp
using System.Threading.Tasks;
using CallgraphClosure.Core;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace MustNotAllocate.Tests;

public class AmortizedAnnotationsFileTests
{
    private const string AnnotationsFile = """
        {
          "amortized_methods": [
            "C.Rent()"
          ]
        }
        """;

    [Fact]
    public async Task MethodListedInAnnotationsFile_IsTreatedAsAmortized()
    {
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotAllocate]
                void Caller() { Rent(); }

                byte[] Rent() => new byte[4096];
            }
            """;

        var test = new CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>.Test
        {
            TestCode = source,
        };
        test.TestState.AdditionalFiles.Add(("amortized-methods.json", AnnotationsFile));
        await test.RunAsync();
    }

    [Fact]
    public async Task MethodNotInFileAndNotAnnotated_StillFiresCGC001()
    {
        var source = """
            using CallgraphClosure.Attributes;

            class C
            {
                [MustNotAllocate]
                void Caller() { UnannotatedUnlisted(); }

                byte[] UnannotatedUnlisted() => new byte[4096];
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SourceBoundary)
            .WithLocation(6, 21)
            .WithArguments("Caller", "MustNotAllocateAttribute", "UnannotatedUnlisted");

        var test = new CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>.Test
        {
            TestCode = source,
        };
        test.TestState.AdditionalFiles.Add(("amortized-methods.json", AnnotationsFile));
        test.ExpectedDiagnostics.Add(expected);
        await test.RunAsync();
    }
}
```

- [ ] **Step 2: Run — first fails, second passes**

Run: `dotnet test tests/MustNotAllocate.Tests/ --filter "FullyQualifiedName~AmortizedAnnotationsFile"`
Expected: first fails (analyzer doesn't yet read AdditionalFiles), second passes.

- [ ] **Step 3: Extend Config with file-name convention**

Overwrite `src/CallgraphClosure.Core/Config.cs`:

```csharp
using System.Collections.Immutable;

namespace CallgraphClosure.Core;

public sealed record Config(
    string AttributeFullName,
    PropagationDirection Direction,
    ImmutableArray<ISink> Sinks,
    string? AmortizedAttributeFullName = null,
    string AmortizedFileName = "amortized-methods.json");
```

The file-name convention defaults to `amortized-methods.json`. Consumers can override via the record's with-expression.

- [ ] **Step 4: Parse the file in CallgraphClosureAnalyzer**

In `src/CallgraphClosure.Core/CallgraphClosureAnalyzer.cs`, change `OnStart` to read AdditionalFiles, parse JSON, and pass the resulting set through to `AnalyzeBlock`. Then update `VisitOp` to check the set before emitting a boundary.

Add a new using: `using System.Collections.Immutable;` (already present).

Also add: `using System.Linq;` (already present).

Replace `OnStart`, `AnalyzeBlock`, and `VisitOp` with:

```csharp
    private void OnStart(CompilationStartAnalysisContext c)
    {
        var attrSym = c.Compilation.GetTypeByMetadataName(_config.AttributeFullName);
        if (attrSym is null) return;

        var amortizedSym = _config.AmortizedAttributeFullName is null
            ? null
            : c.Compilation.GetTypeByMetadataName(_config.AmortizedAttributeFullName);

        var amortizedFileMethods = LoadAmortizedFileMethods(c);

        c.RegisterOperationBlockAction(b =>
            AnalyzeBlock(b, attrSym, amortizedSym, amortizedFileMethods, c.Compilation));
    }

    private ImmutableHashSet<string> LoadAmortizedFileMethods(CompilationStartAnalysisContext c)
    {
        foreach (var file in c.Options.AdditionalFiles)
        {
            if (System.IO.Path.GetFileName(file.Path) != _config.AmortizedFileName)
                continue;

            var text = file.GetText(c.CancellationToken);
            if (text is null) continue;

            try
            {
                return ParseAmortizedJson(text.ToString());
            }
            catch (System.FormatException)
            {
                // Malformed — treat as empty. (A formal diagnostic is out of scope for M2.5.)
                return ImmutableHashSet<string>.Empty;
            }
        }
        return ImmutableHashSet<string>.Empty;
    }

    private static ImmutableHashSet<string> ParseAmortizedJson(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("amortized_methods", out var arr))
            return ImmutableHashSet<string>.Empty;

        var builder = ImmutableHashSet.CreateBuilder<string>();
        foreach (var element in arr.EnumerateArray())
        {
            var name = element.GetString();
            if (!string.IsNullOrWhiteSpace(name))
                builder.Add(name);
        }
        return builder.ToImmutable();
    }

    private void AnalyzeBlock(
        OperationBlockAnalysisContext b,
        INamedTypeSymbol attrSym,
        INamedTypeSymbol? amortizedSym,
        ImmutableHashSet<string> amortizedFileMethods,
        Compilation compilation)
    {
        if (b.OwningSymbol is not IMethodSymbol caller) return;
        if (!HasAttribute(caller, attrSym)) return;

        foreach (var block in b.OperationBlocks)
        {
            foreach (var op in block.DescendantsAndSelf())
            {
                VisitOp(op, caller, attrSym, amortizedSym, amortizedFileMethods, compilation, b);
            }
        }
    }

    private void VisitOp(
        IOperation op,
        IMethodSymbol caller,
        INamedTypeSymbol attrSym,
        INamedTypeSymbol? amortizedSym,
        ImmutableHashSet<string> amortizedFileMethods,
        Compilation compilation,
        OperationBlockAnalysisContext b)
    {
        if (op is IObjectCreationOperation && op.Parent is IAttributeOperation) return;

        foreach (var sink in _config.Sinks)
        {
            var label = sink.Match(op);
            if (label is null) continue;

            b.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.SinkHit,
                op.Syntax.GetLocation(),
                caller.Name,
                attrSym.Name,
                label));
        }

        IMethodSymbol? target = op switch
        {
            IInvocationOperation inv => inv.TargetMethod,
            IObjectCreationOperation oc => oc.Constructor,
            _ => null,
        };

        if (target is null) return;

        var original = target.OriginalDefinition;
        if (HasAttribute(original, attrSym)) return;
        if (amortizedSym is not null && HasAttribute(original, amortizedSym)) return;
        if (amortizedFileMethods.Contains(SymbolFqn(original))) return;

        var isExternal = !SymbolEqualityComparer.Default.Equals(
            original.ContainingAssembly, compilation.Assembly);

        var descriptor = isExternal
            ? Diagnostics.ExternalBoundary
            : Diagnostics.SourceBoundary;

        var targetName = op is IObjectCreationOperation
            ? original.ContainingType.Name
            : original.Name;

        b.ReportDiagnostic(Diagnostic.Create(
            descriptor,
            op.Syntax.GetLocation(),
            caller.Name,
            attrSym.Name,
            targetName));
    }

    private static string SymbolFqn(IMethodSymbol method)
    {
        // Produce a Cecil-compatible FQN: "ContainingType.MethodName(ParamType1, ParamType2)"
        var paramList = string.Join(", ",
            method.Parameters.Select(p => p.Type.ToDisplayString()));
        return $"{method.ContainingType.ToDisplayString()}.{method.Name}({paramList})";
    }
```

Note: `ContainingType.ToDisplayString()` produces the same FQN shape Cecil uses (`Namespace.Type`). Test assumes `C.Rent()` as the key — the containing type `C` with no namespace, method `Rent` with no parameters, no trailing space inside the parens.

- [ ] **Step 5: Run the new tests**

Run: `dotnet test tests/MustNotAllocate.Tests/ --filter "FullyQualifiedName~AmortizedAnnotationsFile"`
Expected: 2 tests pass.

If the first test fails with "diagnostic expected but not produced" OR "expected no diagnostic but got one," the FQN format we produce in `SymbolFqn` doesn't match the JSON key format. Debug by adding a temp `Console.WriteLine` of what FQN is being computed, and adjust either `SymbolFqn` or the test JSON key to match. Report DONE_WITH_CONCERNS if the mismatch requires a non-trivial change.

- [ ] **Step 6: Run full suite**

Run: `dotnet test`
Expected: 41 tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/CallgraphClosure.Core/Config.cs
git add src/CallgraphClosure.Core/CallgraphClosureAnalyzer.cs
git add tests/MustNotAllocate.Tests/AmortizedAnnotationsFileTests.cs
git commit -m "feat(roslyn): read [AdditionalFiles] amortized-methods.json"
```

---

## Task 8: IL CLI --amortized-file flag + ship bcl-amortized.json

Hook the JSON annotations file into the IL CLI via a command-line flag. Pass the resulting set through to the walker. Ship a default `bcl-amortized.json` with the common ArrayPool entries. Update `MustNotAllocate.Sample` to reference it (so the end-to-end shows the upgraded CGC002s for `Console.WriteLine` remain, while an `ArrayPool<T>.Rent` call added to the sample would be quiet).

**Files:**
- Modify: `src/CallgraphClosure.ILCheck.Core/ClosureWalker.cs` (extend to accept an `AmortizedSet` parameter)
- Modify: `src/CallgraphClosure.ILCheck.Cli/Program.cs` (parse --amortized-file arg)
- Create: `src/MustNotAllocate.ILCheck/bcl-amortized.json`
- Create: `tests/MustNotAllocate.ILCheck.Tests/AmortizedAnnotationsFileTests.cs`

- [ ] **Step 1: Write failing test**

Create `tests/MustNotAllocate.ILCheck.Tests/AmortizedAnnotationsFileTests.cs`:

```csharp
using System.Collections.Immutable;
using System.Linq;
using CallgraphClosure.ILCheck.Core;
using MustNotAllocate.ILCheck;
using Mono.Cecil;
using Xunit;

namespace MustNotAllocate.ILCheck.Tests;

public class AmortizedFileTests
{
    [Fact]
    public void MethodInFile_IsTreatedAsAmortized()
    {
        var source = """
            using CallgraphClosure.Attributes;

            public class C
            {
                [MustNotAllocate]
                public void Caller() { Rent(); }

                public byte[] Rent() => new byte[4096];
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var amortized = AmortizedSet.Parse("""{"amortized_methods": ["System.Byte[] C::Rent()"]}""");

        var walker = new ClosureWalker(
            MustNotAllocateIlAnalyzer.AttributeFullName,
            MustNotAllocateIlAnalyzer.Sinks,
            propertyName: "MustNotAllocate",
            amortizedAttributeFullName: MustNotAllocateIlAnalyzer.AmortizedAttributeFullName,
            amortizedSet: amortized);

        var diagnostics = walker.Analyze(assembly);

        Assert.Empty(diagnostics);
    }
}
```

The FQN `"System.Byte[] C::Rent()"` is the Cecil `MethodReference.FullName` format. Cecil's default FQN includes: return type, then `DeclaringType::MethodName(ParamTypes)`. The walker will compare `resolved.FullName` against the set's entries.

- [ ] **Step 2: Run — expect compile error (ClosureWalker constructor doesn't take amortizedSet yet)**

Run: `dotnet build tests/MustNotAllocate.ILCheck.Tests/`
Expected: compile error, missing parameter. Red state.

- [ ] **Step 3: Extend ClosureWalker to accept AmortizedSet**

In `src/CallgraphClosure.ILCheck.Core/ClosureWalker.cs`, add a new field + constructor parameter, and a new check in `VisitMethodBody`:

After `private readonly string? _amortizedAttributeFullName;`, add:

```csharp
    private readonly AmortizedSet _amortizedSet;
```

Update the constructor signature to:

```csharp
    public ClosureWalker(
        string attributeFullName,
        ImmutableArray<IIlSink> sinks,
        string propertyName,
        string? amortizedAttributeFullName = null,
        AmortizedSet? amortizedSet = null)
    {
        _attributeFullName = attributeFullName;
        _sinks = sinks;
        _propertyName = propertyName;
        _amortizedAttributeFullName = amortizedAttributeFullName;
        _amortizedSet = amortizedSet ?? AmortizedSet.Empty;
    }
```

In `VisitMethodBody`, after the existing amortized-attribute check, add the amortized-set check:

```csharp
            if (resolved is not null &&
                _amortizedAttributeFullName is not null &&
                HasAttributeByFullName(resolved, _amortizedAttributeFullName))
                continue;

            if (resolved is not null && _amortizedSet.Contains(resolved.FullName))
                continue;
```

- [ ] **Step 4: Run the test — should pass**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/ --filter "FullyQualifiedName~AmortizedFileTests"`
Expected: PASS.

If the test fails because the FQN format doesn't match, use a debugger or a `Console.WriteLine(resolved.FullName)` in the walker to see what Cecil actually produces, and adjust the test's JSON entry to match. Common Cecil FQN formats:
- `"System.Byte[] C::Rent()"` — return type first, then `Type::Method(ParamTypes)`
- For generics: `"T[] System.Buffers.ArrayPool`1::Rent(System.Int32)"`

- [ ] **Step 5: Add --amortized-file flag to the CLI**

Replace `src/CallgraphClosure.ILCheck.Cli/Program.cs` entirely:

```csharp
using System;
using System.IO;
using CallgraphClosure.ILCheck.Core;
using Mono.Cecil;
using MustNotAllocate.ILCheck;

namespace CallgraphClosure.ILCheck.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        string? assemblyPath = null;
        string? amortizedPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--amortized-file" && i + 1 < args.Length)
            {
                amortizedPath = args[++i];
            }
            else if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                assemblyPath = args[i];
            }
        }

        if (assemblyPath is null)
        {
            Console.Error.WriteLine("Usage: cgc-ilcheck [--amortized-file <path>] <assembly>");
            return 2;
        }

        if (!File.Exists(assemblyPath))
        {
            Console.Error.WriteLine($"Error: file not found: {assemblyPath}");
            return 2;
        }

        AmortizedSet amortized = AmortizedSet.Empty;
        if (amortizedPath is not null)
        {
            if (!File.Exists(amortizedPath))
            {
                Console.Error.WriteLine($"Error: amortized file not found: {amortizedPath}");
                return 2;
            }
            try
            {
                amortized = AmortizedSet.Parse(File.ReadAllText(amortizedPath));
            }
            catch (FormatException ex)
            {
                Console.Error.WriteLine($"Error parsing amortized file: {ex.Message}");
                return 2;
            }
        }

        using var assembly = AssemblyDefinition.ReadAssembly(
            assemblyPath,
            new ReaderParameters
            {
                AssemblyResolver = AssemblyResolver.ForAssemblyPath(assemblyPath),
            });

        var walker = new ClosureWalker(
            MustNotAllocateIlAnalyzer.AttributeFullName,
            MustNotAllocateIlAnalyzer.Sinks,
            propertyName: "MustNotAllocate",
            amortizedAttributeFullName: MustNotAllocateIlAnalyzer.AmortizedAttributeFullName,
            amortizedSet: amortized);

        var diagnostics = walker.Analyze(assembly);

        Console.Write(DiagnosticFormatter.Format(assemblyPath, diagnostics));

        return diagnostics.Length == 0 ? 0 : 1;
    }
}
```

- [ ] **Step 6: Ship bcl-amortized.json**

Create `src/MustNotAllocate.ILCheck/bcl-amortized.json`:

```json
{
  "amortized_methods": [
    "T[] System.Buffers.ArrayPool`1::Rent(System.Int32)",
    "T System.Buffers.MemoryPool`1::Rent(System.Int32)",
    "T[] System.Buffers.SharedArrayPool`1::Rent(System.Int32)"
  ]
}
```

Note: the exact Cecil FQN for `ArrayPool<T>.Rent` depends on how Cecil spells generic return types. If running the CLI against the compiled sample shows that `Rent` isn't being matched, inspect what Cecil actually emits via a temporary `Console.WriteLine(resolved.FullName)` in the walker and adjust this file. Document the adjustment in the commit message.

Update `src/MustNotAllocate.ILCheck/MustNotAllocate.ILCheck.csproj` to copy the JSON next to the DLL at build time:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CallgraphClosure.ILCheck.Core\CallgraphClosure.ILCheck.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="bcl-amortized.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
```

- [ ] **Step 7: Smoke-run the CLI with the default file**

Run:
```bash
dotnet build src/MustNotAllocate.Sample/
dotnet run --project src/CallgraphClosure.ILCheck.Cli/ -- \
  --amortized-file src/MustNotAllocate.ILCheck/bcl-amortized.json \
  src/MustNotAllocate.Sample/bin/Debug/net10.0/MustNotAllocate.Sample.dll 2>&1 | \
  tail -5
```

Expected: the summary line shows CGC003 count lower than without the file (because BCL transitive sinks via ArrayPool branches are now suppressed). The direct `new int[16]` diagnostic is still present.

The exact count depends on what BCL paths are walked. Report the before/after summary line counts.

- [ ] **Step 8: Run full suite**

Run: `dotnet test`
Expected: 42 tests pass (41 + 1 new AmortizedFileTests).

- [ ] **Step 9: Commit**

```bash
git add src/CallgraphClosure.ILCheck.Core/ClosureWalker.cs
git add src/CallgraphClosure.ILCheck.Cli/Program.cs
git add src/MustNotAllocate.ILCheck/bcl-amortized.json
git add src/MustNotAllocate.ILCheck/MustNotAllocate.ILCheck.csproj
git add tests/MustNotAllocate.ILCheck.Tests/AmortizedAnnotationsFileTests.cs
git commit -m "feat(ilcheck): add --amortized-file CLI flag and ship bcl-amortized.json"
```

---

## Task 9: Showcase.Http.Common library (shared types)

The `ParsedRequest` ref struct + small result types used by both Naive and Optimized. Own project so both implementations share exactly one definition.

**Files:**
- Create: `src/Showcase.Http.Common/Showcase.Http.Common.csproj`
- Create: `src/Showcase.Http.Common/ParsedRequest.cs`

- [ ] **Step 1: Create the project**

Run:
```bash
dotnet new classlib -o src/Showcase.Http.Common -f net10.0
rm src/Showcase.Http.Common/Class1.cs
dotnet sln add src/Showcase.Http.Common/Showcase.Http.Common.csproj
```

- [ ] **Step 2: Configure .csproj**

Overwrite `src/Showcase.Http.Common/Showcase.Http.Common.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

`TreatWarningsAsErrors=false` because the Naive variant's analyzer warnings will propagate here if we're not careful; we want those warnings visible in the Naive project specifically, not swallowed silently.

- [ ] **Step 3: Write ParsedRequest**

Create `src/Showcase.Http.Common/ParsedRequest.cs`:

```csharp
using System;

namespace Showcase.Http.Common;

// Naive variant: class-wrapped strings, heap-allocated per parse.
public sealed class NaiveParsedRequest
{
    public string Method { get; }
    public string Path { get; }
    public string Query { get; }
    public string Version { get; }

    public NaiveParsedRequest(string method, string path, string query, string version)
    {
        Method = method;
        Path = path;
        Query = query;
        Version = version;
    }
}

// Optimized variant: ref struct over the original buffer, zero allocation.
public readonly ref struct OptimizedParsedRequest
{
    public ReadOnlySpan<byte> Method { get; }
    public ReadOnlySpan<byte> Path { get; }
    public ReadOnlySpan<byte> Query { get; }
    public ReadOnlySpan<byte> Version { get; }

    public OptimizedParsedRequest(
        ReadOnlySpan<byte> method,
        ReadOnlySpan<byte> path,
        ReadOnlySpan<byte> query,
        ReadOnlySpan<byte> version)
    {
        Method = method;
        Path = path;
        Query = query;
        Version = version;
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Showcase.Http.Common/`
Expected: 0/0.

- [ ] **Step 5: Commit**

```bash
git add src/Showcase.Http.Common/
git commit -m "feat(showcase): add ParsedRequest types for naive and optimized variants"
```

---

## Task 10: Showcase.Http.Naive — parser + reader with intentional allocations

The Naive variant's implementation. Uses `string.Split`, `Substring`, class instantiation — all classic allocation traps. Applies `[MustNotAllocate]` to parse and read methods so the analyzer lights up.

**Files:**
- Create: `src/Showcase.Http.Naive/Showcase.Http.Naive.csproj`
- Create: `src/Showcase.Http.Naive/RequestLineParser.cs`
- Create: `src/Showcase.Http.Naive/RequestReader.cs`

- [ ] **Step 1: Create the project**

Run:
```bash
dotnet new classlib -o src/Showcase.Http.Naive -f net10.0
rm src/Showcase.Http.Naive/Class1.cs
dotnet sln add src/Showcase.Http.Naive/Showcase.Http.Naive.csproj
```

- [ ] **Step 2: Configure .csproj**

Overwrite `src/Showcase.Http.Naive/Showcase.Http.Naive.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Showcase.Http.Common\Showcase.Http.Common.csproj" />
    <ProjectReference Include="..\CallgraphClosure.Attributes\CallgraphClosure.Attributes.csproj" />
    <ProjectReference Include="..\MustNotAllocate\MustNotAllocate.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
    <ProjectReference Include="..\CallgraphClosure.Core\CallgraphClosure.Core.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

The two analyzer ProjectReferences match the pattern from `MustNotAllocate.Sample` (the fragile workaround documented in `known_issues.md`). If the analyzer doesn't fire, apply the same absolute-path `<Analyzer>` fallback pattern the sample uses.

- [ ] **Step 3: Write RequestLineParser**

Create `src/Showcase.Http.Naive/RequestLineParser.cs`:

```csharp
using System;
using CallgraphClosure.Attributes;
using Showcase.Http.Common;

namespace Showcase.Http.Naive;

public static class RequestLineParser
{
    [MustNotAllocate]
    public static NaiveParsedRequest Parse(string line)
    {
        // Allocations all over the place — every one is intentional.
        var parts = line.Split(' ');         // new string[] + N substring allocations
        if (parts.Length != 3)
            throw new FormatException("Malformed request line");

        var method = parts[0];
        var target = parts[1];
        var version = parts[2];

        string path;
        string query;
        var queryIdx = target.IndexOf('?');
        if (queryIdx >= 0)
        {
            path = target.Substring(0, queryIdx);   // new string
            query = target.Substring(queryIdx + 1); // new string
        }
        else
        {
            path = target;
            query = string.Empty;
        }

        return new NaiveParsedRequest(method, path, query, version);  // new class
    }
}
```

- [ ] **Step 4: Write RequestReader**

Create `src/Showcase.Http.Naive/RequestReader.cs`:

```csharp
using System.IO;
using System.Text;
using CallgraphClosure.Attributes;
using Showcase.Http.Common;

namespace Showcase.Http.Naive;

public sealed class RequestReader
{
    private const int BufferSize = 4096;

    [MustNotAllocate]
    public NaiveParsedRequest ReadNext(Stream input)
    {
        var buffer = new byte[BufferSize];   // per-call heap array — CGC003
        var bytesRead = input.Read(buffer, 0, BufferSize);
        var line = Encoding.UTF8.GetString(buffer, 0, bytesRead); // allocates a string

        // Strip trailing CRLF for parsing.
        var eol = line.IndexOf('\r');
        if (eol < 0) eol = line.Length;

        return RequestLineParser.Parse(line.Substring(0, eol));  // another substring
    }
}
```

- [ ] **Step 5: Build and observe diagnostics**

Run: `dotnet build src/Showcase.Http.Naive/ 2>&1 | grep CGC | head -20`
Expected: multiple CGC003s on the allocations, possibly some CGC002s on `string.Split`, `Substring`, `Encoding.UTF8.GetString`.

**Save the full list of warnings** — we'll commit it as `Showcase.Http.Naive.expected.txt` in Task 12.

- [ ] **Step 6: Commit**

```bash
git add src/Showcase.Http.Naive/
git commit -m "feat(showcase): add naive HTTP request-line parser with intentional allocations"
```

---

## Task 11: Showcase.Http.Optimized — Span + ArrayPool, analyzer clean

The Optimized variant: `ReadOnlySpan<byte>`-based parsing, `OptimizedParsedRequest` ref struct, `ArrayPool<byte>.Shared.Rent` in the reader. With `bcl-amortized.json` wired, the analyzer should emit zero diagnostics.

**Files:**
- Create: `src/Showcase.Http.Optimized/Showcase.Http.Optimized.csproj`
- Create: `src/Showcase.Http.Optimized/RequestLineParser.cs`
- Create: `src/Showcase.Http.Optimized/RequestReader.cs`
- Create: `src/Showcase.Http.Optimized/amortized-methods.json`

- [ ] **Step 1: Create the project**

Run:
```bash
dotnet new classlib -o src/Showcase.Http.Optimized -f net10.0
rm src/Showcase.Http.Optimized/Class1.cs
dotnet sln add src/Showcase.Http.Optimized/Showcase.Http.Optimized.csproj
```

- [ ] **Step 2: Configure .csproj — same analyzer wiring + AdditionalFiles for annotations**

Overwrite `src/Showcase.Http.Optimized/Showcase.Http.Optimized.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Showcase.Http.Common\Showcase.Http.Common.csproj" />
    <ProjectReference Include="..\CallgraphClosure.Attributes\CallgraphClosure.Attributes.csproj" />
    <ProjectReference Include="..\MustNotAllocate\MustNotAllocate.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
    <ProjectReference Include="..\CallgraphClosure.Core\CallgraphClosure.Core.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
  <ItemGroup>
    <AdditionalFiles Include="amortized-methods.json" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write the local annotations file**

Create `src/Showcase.Http.Optimized/amortized-methods.json`:

```json
{
  "amortized_methods": [
    "System.Buffers.ArrayPool<T>.Rent(int)"
  ]
}
```

The FQN format here is what the Roslyn analyzer produces via `SymbolFqn` (from Task 7). Two subtleties worth noting:

1. **`<T>` not `<byte>`.** The walker strips the target to `.OriginalDefinition` before computing the FQN — for a generic method, that's the unbound form (`ArrayPool<T>.Rent(int)`), not the constructed form (`ArrayPool<byte>.Rent(int)`). Same mechanism that makes M1's generic-callee tests pass.
2. **Short `int` not `System.Int32`.** `ToDisplayString()` uses language keywords for primitive types by default.

If the first build shows the analyzer still firing on `Rent`, dump the actual FQN via a temporary `Console.WriteLine` in `SymbolFqn` and adjust the JSON entry to match.

**Heads-up about `get_Shared`:** `ArrayPool<byte>.Shared` is a static property access; in IL it compiles to a `call` of `get_Shared()`. The walker may flag this as an unannotated external call. If it does, add `"System.Buffers.ArrayPool<T>.get_Shared()"` to the JSON too.

- [ ] **Step 4: Write RequestLineParser (Span-based)**

Create `src/Showcase.Http.Optimized/RequestLineParser.cs`:

```csharp
using System;
using CallgraphClosure.Attributes;
using Showcase.Http.Common;

namespace Showcase.Http.Optimized;

public static class RequestLineParser
{
    [MustNotAllocate]
    public static OptimizedParsedRequest Parse(ReadOnlySpan<byte> line)
    {
        // Strip trailing CRLF.
        var eol = line.IndexOf((byte)'\r');
        if (eol >= 0) line = line.Slice(0, eol);

        var firstSpace = line.IndexOf((byte)' ');
        if (firstSpace < 0) throw new FormatException("Malformed request line");
        var method = line.Slice(0, firstSpace);

        var rest = line.Slice(firstSpace + 1);
        var secondSpace = rest.IndexOf((byte)' ');
        if (secondSpace < 0) throw new FormatException("Malformed request line");
        var target = rest.Slice(0, secondSpace);
        var version = rest.Slice(secondSpace + 1);

        ReadOnlySpan<byte> path;
        ReadOnlySpan<byte> query;
        var queryIdx = target.IndexOf((byte)'?');
        if (queryIdx >= 0)
        {
            path = target.Slice(0, queryIdx);
            query = target.Slice(queryIdx + 1);
        }
        else
        {
            path = target;
            query = default;
        }

        return new OptimizedParsedRequest(method, path, query, version);
    }
}
```

- [ ] **Step 5: Write RequestReader (pool-backed)**

Create `src/Showcase.Http.Optimized/RequestReader.cs`:

```csharp
using System;
using System.Buffers;
using System.IO;
using CallgraphClosure.Attributes;
using Showcase.Http.Common;

namespace Showcase.Http.Optimized;

public sealed class RequestReader
{
    private const int BufferSize = 4096;

    // Callers must consume the returned ref struct before returning from their frame.
    // The caller is responsible for the try/finally discipline around the lease.
    [MustNotAllocate]
    public BufferLease ReadNext(Stream input)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var bytesRead = input.Read(buffer, 0, BufferSize);
        return new BufferLease(buffer, bytesRead);
    }
}

public readonly struct BufferLease : IDisposable
{
    private readonly byte[] _buffer;
    public int Length { get; }

    internal BufferLease(byte[] buffer, int length)
    {
        _buffer = buffer;
        Length = length;
    }

    public ReadOnlySpan<byte> AsSpan() => _buffer.AsSpan(0, Length);

    public void Dispose() => ArrayPool<byte>.Shared.Return(_buffer);
}
```

Note: the `BufferLease` struct is **value-typed**, so `new BufferLease(...)` doesn't heap-allocate. Its `Dispose` returns the buffer to the pool; callers use `using var lease = reader.ReadNext(stream);`.

- [ ] **Step 6: Build and observe**

Run: `dotnet build src/Showcase.Http.Optimized/ 2>&1 | grep CGC`
Expected: **zero CGC warnings**. If any fire, triage:

- CGC003 on `new BufferLease(...)`: shouldn't happen because `BufferLease` is a struct. Verify `readonly struct` declaration is intact.
- CGC002 or CGC001 on `ArrayPool<byte>.Shared.Rent(...)`: the annotations file isn't being matched. Dump the actual FQN the analyzer sees (temporary `Console.Error.WriteLine` in `SymbolFqn`) and adjust `amortized-methods.json` to match.
- CGC001 on `ArrayPool<byte>.Shared`: property access (`get_Shared`) is a call; add its FQN to the JSON too. Pattern: `"System.Buffers.ArrayPool<byte>.get_Shared()"`.
- CGC001/002 on `input.Read(...)`: `Stream.Read` is external and transitively may allocate. For the showcase, we accept the reader's analyzer output may have a couple of entries *about the Stream read itself* — document what shows up; the core demonstration (pool vs naive-new) is preserved.

Document whatever final state is achieved (zero warnings ideal, small count tolerable).

- [ ] **Step 7: Commit**

```bash
git add src/Showcase.Http.Optimized/
git commit -m "feat(showcase): add optimized HTTP parser using Span + ArrayPool"
```

---

## Task 12: Commit expected analyzer outputs for diff-ability

Capture the analyzer output of each variant into committed `.expected.txt` files, so a reviewer can diff Naive vs Optimized without running the tool.

**Files:**
- Create: `src/Showcase.Http.Naive/Showcase.Http.Naive.expected.txt`
- Create: `src/Showcase.Http.Optimized/Showcase.Http.Optimized.expected.txt`

- [ ] **Step 1: Capture Naive output**

Run:
```bash
dotnet build src/Showcase.Http.Naive/ 2>&1 | \
  grep "warning CGC" | \
  sed 's|/mnt/c/work/dotnet-callgraph-closure/||' | \
  sort > src/Showcase.Http.Naive/Showcase.Http.Naive.expected.txt
```

The `sed` strips the absolute path prefix; the `sort` makes the output deterministic.

Inspect the file:

```bash
cat src/Showcase.Http.Naive/Showcase.Http.Naive.expected.txt
```

Expected: several lines of CGC003 (allocations) and possibly CGC002 (external calls). Each line looks like:

```
src/Showcase.Http.Naive/RequestLineParser.cs(13,17): warning CGC003: Method 'Parse' is annotated [MustNotAllocateAttribute] but contains a array allocation [src/Showcase.Http.Naive/Showcase.Http.Naive.csproj]
```

- [ ] **Step 2: Capture Optimized output**

Run:
```bash
dotnet build src/Showcase.Http.Optimized/ 2>&1 | \
  grep "warning CGC" | \
  sed 's|/mnt/c/work/dotnet-callgraph-closure/||' | \
  sort > src/Showcase.Http.Optimized/Showcase.Http.Optimized.expected.txt
```

Expected: empty or nearly-empty file. Inspect it:

```bash
cat src/Showcase.Http.Optimized/Showcase.Http.Optimized.expected.txt
```

If the file is non-empty, each entry is either a real violation (fix the code) or a case `bcl-amortized.json` doesn't cover (add to the JSON, rebuild, re-capture).

- [ ] **Step 3: Commit**

```bash
git add src/Showcase.Http.Naive/Showcase.Http.Naive.expected.txt
git add src/Showcase.Http.Optimized/Showcase.Http.Optimized.expected.txt
git commit -m "docs(showcase): commit expected analyzer outputs for naive/optimized diff"
```

---

## Task 13: BenchmarkDotNet project + milestone tag

Four benchmarks: `NaiveParse`, `OptimizedParse`, `NaiveRead`, `OptimizedRead`. Commit one baseline run's results. Tag `m2.5-complete`.

**Files:**
- Create: `bench/Showcase.Http.Benchmarks/Showcase.Http.Benchmarks.csproj`
- Create: `bench/Showcase.Http.Benchmarks/Program.cs`
- Create: `bench/Showcase.Http.Benchmarks/Benchmarks.cs`
- Create: `bench/Showcase.Http.Benchmarks/baseline-results.md` (committed output)

- [ ] **Step 1: Create the project**

Run:
```bash
mkdir -p bench
dotnet new console -o bench/Showcase.Http.Benchmarks -f net10.0
rm bench/Showcase.Http.Benchmarks/Program.cs
dotnet sln add bench/Showcase.Http.Benchmarks/Showcase.Http.Benchmarks.csproj
```

- [ ] **Step 2: Configure .csproj**

Overwrite `bench/Showcase.Http.Benchmarks/Showcase.Http.Benchmarks.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.13.12" />
    <ProjectReference Include="..\..\src\Showcase.Http.Naive\Showcase.Http.Naive.csproj" />
    <ProjectReference Include="..\..\src\Showcase.Http.Optimized\Showcase.Http.Optimized.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write Benchmarks.cs**

Create `bench/Showcase.Http.Benchmarks/Benchmarks.cs`:

```csharp
using System.IO;
using System.Text;
using BenchmarkDotNet.Attributes;

namespace Showcase.Http.Benchmarks;

[MemoryDiagnoser]
public class ParseBenchmarks
{
    private const string RequestLine = "GET /users?id=42&sort=asc HTTP/1.1";
    private readonly byte[] _bytes = Encoding.UTF8.GetBytes(RequestLine);

    [Benchmark(Baseline = true)]
    public int Naive()
    {
        var req = Naive.RequestLineParser.Parse(RequestLine);
        return req.Method.Length + req.Path.Length + req.Query.Length + req.Version.Length;
    }

    [Benchmark]
    public int Optimized()
    {
        var req = Optimized.RequestLineParser.Parse(_bytes);
        return req.Method.Length + req.Path.Length + req.Query.Length + req.Version.Length;
    }
}

[MemoryDiagnoser]
public class ReadBenchmarks
{
    private const string RequestLine = "GET /users?id=42&sort=asc HTTP/1.1\r\n";
    private readonly byte[] _requestBytes = Encoding.UTF8.GetBytes(RequestLine);
    private readonly Naive.RequestReader _naiveReader = new();
    private readonly Optimized.RequestReader _optimizedReader = new();

    [Benchmark(Baseline = true)]
    public int Naive()
    {
        using var stream = new MemoryStream(_requestBytes);
        var req = _naiveReader.ReadNext(stream);
        return req.Path.Length;
    }

    [Benchmark]
    public int Optimized()
    {
        using var stream = new MemoryStream(_requestBytes);
        using var lease = _optimizedReader.ReadNext(stream);
        var req = Optimized.RequestLineParser.Parse(lease.AsSpan());
        return req.Path.Length;
    }
}
```

- [ ] **Step 4: Write Program.cs**

Create `bench/Showcase.Http.Benchmarks/Program.cs`:

```csharp
using BenchmarkDotNet.Running;

namespace Showcase.Http.Benchmarks;

public static class Program
{
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
```

- [ ] **Step 5: Build, then run the benchmarks**

Run:
```bash
dotnet build -c Release bench/Showcase.Http.Benchmarks/
dotnet run -c Release --project bench/Showcase.Http.Benchmarks/ -- --filter '*' 2>&1 | tee /tmp/bench-output.txt
```

Benchmarks take 1-5 minutes. Be patient.

Capture the results summary tables (the `|` ASCII tables BenchmarkDotNet prints near the end). The MemoryDiagnoser columns will include `Allocated` and `Gen 0` / `Gen 1` / `Gen 2`.

- [ ] **Step 6: Commit the baseline results**

Create `bench/Showcase.Http.Benchmarks/baseline-results.md` and paste the results tables from the benchmark output. Format:

```markdown
# Showcase.Http.Benchmarks — Baseline Results

Captured: 2026-04-XX on <hardware/runtime-info>

## ParseBenchmarks

<paste the BDN results table here>

## ReadBenchmarks

<paste the BDN results table here>

## Summary

- Parse: Optimized is ~<X>x faster and allocates <Y>B vs <Z>B
- Read: Optimized is ~<X>x faster and allocates 0B vs ~4KB per call
```

The specific numbers depend on the machine. Expected shape: Optimized is order-of-magnitude faster and allocates zero (or near-zero) bytes.

- [ ] **Step 7: Verify everything still builds + tests still pass**

Run:
```bash
dotnet build CallgraphClosure.sln
dotnet test
```

Expected: solution builds; all tests pass.

- [ ] **Step 8: Tag the milestone**

Run: `git tag -a m2.5-complete -m "Milestone 2.5: [AmortizedAllocation] attribute, annotations file, HTTP showcase with benchmarks"`

Verify: `git tag --list`
Expected: `m1-complete`, `m2-complete`, `m2.5-complete`.

- [ ] **Step 9: Commit**

```bash
git add bench/Showcase.Http.Benchmarks/
git commit -m "feat(bench): add BenchmarkDotNet comparison of naive vs optimized HTTP parser"
```
