# Callgraph-Closure Lint M1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Roslyn analyzer that flags direct call-boundary violations and allocation sinks inside methods annotated with `[MustNotAllocate]`, with three diagnostic IDs (CGC001 source-boundary, CGC002 external-boundary, CGC003 sink).

**Architecture:** Two-layer split: a reusable property-agnostic `CallgraphClosureAnalyzer` abstract base in `CallgraphClosure.Core` (walks `IOperation` inside annotated methods, dispatches to configured sinks and classifies call boundaries), and a concrete `MustNotAllocateAnalyzer` that binds the attribute FQN plus object/array/boxing sinks. Algorithm is per-method local; propagation is emergent via cascading fixes.

**Tech Stack:** .NET Roslyn (Microsoft.CodeAnalysis.CSharp 4.8), analyzer projects target netstandard2.0, tests use xUnit + Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit 1.1.2, sample app net8.0.

**Reference spec:** `docs/superpowers/specs/2026-04-16-callgraph-closure-m1-design.md`

---

## Task 1: Repo bootstrap

**Files:**
- Create: `.gitignore`
- Create: `README.md`
- Create: `CallgraphClosure.sln`
- Create: `Directory.Build.props`
- Create: `src/` and `tests/` directories

- [ ] **Step 1: Initialize git repo**

Run: `git init`
Expected: "Initialized empty Git repository in /mnt/c/work/dotnet-callgraph-closure/.git/"

- [ ] **Step 2: Write .gitignore**

Create `.gitignore`:

```gitignore
# .NET build output
bin/
obj/

# Visual Studio
.vs/
*.user
*.suo

# Rider / VS Code
.idea/
.vscode/

# NuGet
*.nupkg
packages/

# OS
.DS_Store
Thumbs.db
```

- [ ] **Step 3: Write minimal README**

Create `README.md`:

```markdown
# dotnet-callgraph-closure

A .NET analog of Ferrocene's callgraph-closure lint. Milestone 1 is a Roslyn
analyzer that enforces `[MustNotAllocate]` across direct method calls.

See `docs/superpowers/specs/` for design, `docs/superpowers/plans/` for the
implementation plan.
```

- [ ] **Step 4: Write Directory.Build.props for shared settings**

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

- [ ] **Step 5: Create solution file and directory structure**

Run:
```bash
mkdir -p src tests docs/superpowers/specs docs/superpowers/plans
dotnet new sln -n CallgraphClosure
```

Expected: `CallgraphClosure.sln` appears in repo root.

- [ ] **Step 6: Commit**

```bash
git add .gitignore README.md Directory.Build.props CallgraphClosure.sln
git add docs/
git commit -m "chore: bootstrap repo with sln and shared build props"
```

---

## Task 2: CallgraphClosure.Core scaffolding

**Files:**
- Create: `src/CallgraphClosure.Core/CallgraphClosure.Core.csproj`
- Create: `src/CallgraphClosure.Core/PropagationDirection.cs`
- Create: `src/CallgraphClosure.Core/Config.cs`
- Create: `src/CallgraphClosure.Core/ISink.cs`
- Create: `src/CallgraphClosure.Core/Diagnostics.cs`

- [ ] **Step 1: Create the analyzer library project**

Run:
```bash
dotnet new classlib -o src/CallgraphClosure.Core -f netstandard2.0
rm src/CallgraphClosure.Core/Class1.cs
```

- [ ] **Step 2: Configure the .csproj with analyzer SDK**

Overwrite `src/CallgraphClosure.Core/CallgraphClosure.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <IsRoslynComponent>true</IsRoslynComponent>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.11.0" PrivateAssets="all">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add project to solution**

Run: `dotnet sln add src/CallgraphClosure.Core/CallgraphClosure.Core.csproj`

- [ ] **Step 4: Write PropagationDirection.cs**

Create `src/CallgraphClosure.Core/PropagationDirection.cs`:

```csharp
namespace CallgraphClosure.Core;

public enum PropagationDirection
{
    Downward,
    // Upward reserved for future work.
}
```

- [ ] **Step 5: Write ISink.cs**

Create `src/CallgraphClosure.Core/ISink.cs`:

```csharp
using Microsoft.CodeAnalysis;

namespace CallgraphClosure.Core;

public interface ISink
{
    // Returns a label (e.g. "object", "array", "boxing") if this sink matches the op,
    // otherwise null.
    string? Match(IOperation op);
}
```

- [ ] **Step 6: Write Config.cs**

Create `src/CallgraphClosure.Core/Config.cs`:

```csharp
using System.Collections.Immutable;

namespace CallgraphClosure.Core;

public sealed record Config(
    string AttributeFullName,
    PropagationDirection Direction,
    ImmutableArray<ISink> Sinks);
```

- [ ] **Step 7: Write Diagnostics.cs**

Create `src/CallgraphClosure.Core/Diagnostics.cs`:

```csharp
using Microsoft.CodeAnalysis;

namespace CallgraphClosure.Core;

public static class Diagnostics
{
    private const string Category = "CallgraphClosure";

    public static readonly DiagnosticDescriptor SourceBoundary = new(
        id: "CGC001",
        title: "Annotated method calls unannotated source method",
        messageFormat: "Method '{0}' is annotated [{1}] but calls unannotated method '{2}'. Annotate '{2}' or remove the call.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ExternalBoundary = new(
        id: "CGC002",
        title: "Annotated method calls unannotated external method",
        messageFormat: "Method '{0}' is annotated [{1}] but calls external method '{2}' whose annotation status cannot be verified at edit time. This will be resolved by the IL post-pass.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SinkHit = new(
        id: "CGC003",
        title: "Annotated method contains a property-specific sink",
        messageFormat: "Method '{0}' is annotated [{1}] but contains a {2} allocation.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
```

- [ ] **Step 8: Verify it builds**

Run: `dotnet build src/CallgraphClosure.Core/`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 9: Commit**

```bash
git add src/CallgraphClosure.Core/
git commit -m "feat(core): add scaffolding - config, direction, ISink, diagnostics"
```

---

## Task 3: CallgraphClosureAnalyzer skeleton (no-op)

Implements the abstract base with registration and the "attribute not in compilation" bailout. Has no op-walking yet — intentionally minimal so subsequent tasks can add behavior test-first.

**Files:**
- Create: `src/CallgraphClosure.Core/CallgraphClosureAnalyzer.cs`

- [ ] **Step 1: Write the skeleton**

Create `src/CallgraphClosure.Core/CallgraphClosureAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

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

        c.RegisterOperationBlockAction(b => AnalyzeBlock(b, attrSym, c.Compilation));
    }

    private void AnalyzeBlock(
        OperationBlockAnalysisContext b,
        INamedTypeSymbol attrSym,
        Compilation compilation)
    {
        // Implemented in later tasks.
    }
}
```

- [ ] **Step 2: Verify it builds**

Run: `dotnet build src/CallgraphClosure.Core/`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Commit**

```bash
git add src/CallgraphClosure.Core/CallgraphClosureAnalyzer.cs
git commit -m "feat(core): add analyzer skeleton with no-op AnalyzeBlock"
```

---

## Task 4: MustNotAllocate project with attribute and analyzer wiring

**Files:**
- Create: `src/MustNotAllocate/MustNotAllocate.csproj`
- Create: `src/MustNotAllocate/MustNotAllocateAttribute.cs`
- Create: `src/MustNotAllocate/MustNotAllocateAnalyzer.cs`

- [ ] **Step 1: Create the project**

Run:
```bash
dotnet new classlib -o src/MustNotAllocate -f netstandard2.0
rm src/MustNotAllocate/Class1.cs
dotnet sln add src/MustNotAllocate/MustNotAllocate.csproj
```

- [ ] **Step 2: Configure the .csproj**

Overwrite `src/MustNotAllocate/MustNotAllocate.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <IsRoslynComponent>true</IsRoslynComponent>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" PrivateAssets="all" />
    <ProjectReference Include="..\CallgraphClosure.Core\CallgraphClosure.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write the attribute type**

Create `src/MustNotAllocate/MustNotAllocateAttribute.cs`:

```csharp
using System;

namespace MustNotAllocate;

[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Constructor,
    AllowMultiple = false,
    Inherited = false)]
public sealed class MustNotAllocateAttribute : Attribute { }
```

- [ ] **Step 4: Write the concrete analyzer with empty sink list**

Create `src/MustNotAllocate/MustNotAllocateAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using CallgraphClosure.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MustNotAllocate;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MustNotAllocateAnalyzer : CallgraphClosureAnalyzer
{
    public MustNotAllocateAnalyzer() : base(new Config(
        AttributeFullName: "MustNotAllocate.MustNotAllocateAttribute",
        Direction: PropagationDirection.Downward,
        Sinks: ImmutableArray<ISink>.Empty)) { }
}
```

- [ ] **Step 5: Verify build**

Run: `dotnet build src/MustNotAllocate/`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 6: Commit**

```bash
git add src/MustNotAllocate/
git commit -m "feat(must-not-allocate): add attribute type and analyzer wiring"
```

---

## Task 5: Test project + first smoke test

Sets up the analyzer testing harness and proves the plumbing works end-to-end with a trivial "silent when no annotations present" test.

**Files:**
- Create: `tests/MustNotAllocate.Tests/MustNotAllocate.Tests.csproj`
- Create: `tests/MustNotAllocate.Tests/CSharpAnalyzerVerifier.cs`
- Create: `tests/MustNotAllocate.Tests/SmokeTests.cs`

- [ ] **Step 1: Create the test project**

Run:
```bash
dotnet new xunit -o tests/MustNotAllocate.Tests -f net8.0
rm tests/MustNotAllocate.Tests/UnitTest1.cs
dotnet sln add tests/MustNotAllocate.Tests/MustNotAllocate.Tests.csproj
```

- [ ] **Step 2: Configure the .csproj**

Overwrite `tests/MustNotAllocate.Tests/MustNotAllocate.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.9.0" />
    <PackageReference Include="xunit" Version="2.7.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.7" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit" Version="1.1.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\MustNotAllocate\MustNotAllocate.csproj" />
    <ProjectReference Include="..\..\src\CallgraphClosure.Core\CallgraphClosure.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write the verifier helper**

Create `tests/MustNotAllocate.Tests/CSharpAnalyzerVerifier.cs`:

```csharp
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;

namespace MustNotAllocate.Tests;

public static class CSharpAnalyzerVerifier<TAnalyzer>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    public sealed class Test : CSharpAnalyzerTest<TAnalyzer, XUnitVerifier>
    {
        public Test()
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
            TestState.AdditionalReferences.Add(
                MetadataReference.CreateFromFile(
                    typeof(MustNotAllocateAttribute).Assembly.Location));
        }
    }

    public static DiagnosticResult Diagnostic(DiagnosticDescriptor descriptor) =>
        new(descriptor);

    public static async Task VerifyAnalyzerAsync(
        string source,
        params DiagnosticResult[] expected)
    {
        var test = new Test { TestCode = source };
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }
}
```

- [ ] **Step 4: Write the smoke test**

Create `tests/MustNotAllocate.Tests/SmokeTests.cs`:

```csharp
using System.Threading.Tasks;
using Xunit;

namespace MustNotAllocate.Tests;

public class SmokeTests
{
    [Fact]
    public async Task SourceWithNoAnnotations_ProducesNoDiagnostics()
    {
        var source = """
            using MustNotAllocate;

            class C
            {
                void Caller() { Callee(); }
                void Callee() { }
            }
            """;

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source);
    }
}
```

- [ ] **Step 5: Run the test**

Run: `dotnet test tests/MustNotAllocate.Tests/`
Expected: `Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1`

- [ ] **Step 6: Commit**

```bash
git add tests/MustNotAllocate.Tests/
git commit -m "test: add analyzer test harness and smoke test"
```

---

## Task 6: CGC001 — source call boundary (TDD)

Test-first addition of the invocation-boundary logic to the abstract base.

**Files:**
- Create: `tests/MustNotAllocate.Tests/CGC001_SourceBoundaryTests.cs`
- Modify: `src/CallgraphClosure.Core/CallgraphClosureAnalyzer.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/MustNotAllocate.Tests/CGC001_SourceBoundaryTests.cs`:

```csharp
using System.Threading.Tasks;
using CallgraphClosure.Core;
using Xunit;

namespace MustNotAllocate.Tests;

public class CGC001_SourceBoundaryTests
{
    [Fact]
    public async Task AnnotatedMethod_CallsUnannotatedSourceMethod_FiresCGC001()
    {
        var source = """
            using MustNotAllocate;

            class C
            {
                [MustNotAllocate]
                void Caller() { Callee(); }

                void Callee() { }
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SourceBoundary)
            .WithLocation(6, 21)
            .WithArguments("Caller", "MustNotAllocateAttribute", "Callee");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task AnnotatedMethod_CallsAnnotatedSourceMethod_FiresNothing()
    {
        var source = """
            using MustNotAllocate;

            class C
            {
                [MustNotAllocate]
                void Caller() { Callee(); }

                [MustNotAllocate]
                void Callee() { }
            }
            """;

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task UnannotatedMethod_CallsUnannotatedSourceMethod_FiresNothing()
    {
        var source = """
            using MustNotAllocate;

            class C
            {
                void Caller() { Callee(); }
                void Callee() { }
            }
            """;

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/MustNotAllocate.Tests/ --filter "FullyQualifiedName~CGC001"`
Expected: First test FAILS with something like "Expected diagnostic CGC001 was not produced." Second and third PASS (because no diagnostics are produced either way with the current no-op analyzer).

- [ ] **Step 3: Implement the invocation-boundary logic**

Overwrite `src/CallgraphClosure.Core/CallgraphClosureAnalyzer.cs`:

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

        c.RegisterOperationBlockAction(b => AnalyzeBlock(b, attrSym, c.Compilation));
    }

    private void AnalyzeBlock(
        OperationBlockAnalysisContext b,
        INamedTypeSymbol attrSym,
        Compilation compilation)
    {
        if (b.OwningSymbol is not IMethodSymbol caller) return;
        if (!HasAttribute(caller, attrSym)) return;

        foreach (var block in b.OperationBlocks)
        {
            foreach (var op in block.DescendantsAndSelf())
            {
                VisitOp(op, caller, attrSym, compilation, b);
            }
        }
    }

    private void VisitOp(
        IOperation op,
        IMethodSymbol caller,
        INamedTypeSymbol attrSym,
        Compilation compilation,
        OperationBlockAnalysisContext b)
    {
        var target = op switch
        {
            IInvocationOperation inv => inv.TargetMethod,
            _ => null,
        };

        if (target is null) return;

        var original = target.OriginalDefinition;
        if (HasAttribute(original, attrSym)) return;

        var isExternal = !SymbolEqualityComparer.Default.Equals(
            original.ContainingAssembly, compilation.Assembly);

        var descriptor = isExternal
            ? Diagnostics.ExternalBoundary
            : Diagnostics.SourceBoundary;

        b.ReportDiagnostic(Diagnostic.Create(
            descriptor,
            op.Syntax.GetLocation(),
            caller.Name,
            attrSym.Name,
            original.Name));
    }

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attrSym) =>
        symbol.GetAttributes().Any(a =>
            SymbolEqualityComparer.Default.Equals(a.AttributeClass, attrSym));
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/MustNotAllocate.Tests/ --filter "FullyQualifiedName~CGC001"`
Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0`

- [ ] **Step 5: Run the full suite to confirm no regressions**

Run: `dotnet test tests/MustNotAllocate.Tests/`
Expected: All tests pass (4 total: SmokeTests + CGC001 * 3).

- [ ] **Step 6: Commit**

```bash
git add src/CallgraphClosure.Core/CallgraphClosureAnalyzer.cs
git add tests/MustNotAllocate.Tests/CGC001_SourceBoundaryTests.cs
git commit -m "feat(core): add CGC001 source call boundary diagnostic"
```

---

## Task 7: CGC002 — external call boundary (TDD)

The core already has external detection from Task 6. This task adds the test that locks in CGC002 semantics specifically and verifies external calls use the Info-severity descriptor.

**Files:**
- Create: `tests/MustNotAllocate.Tests/CGC002_ExternalBoundaryTests.cs`

- [ ] **Step 1: Write the test**

Create `tests/MustNotAllocate.Tests/CGC002_ExternalBoundaryTests.cs`:

```csharp
using System.Threading.Tasks;
using CallgraphClosure.Core;
using Xunit;

namespace MustNotAllocate.Tests;

public class CGC002_ExternalBoundaryTests
{
    [Fact]
    public async Task AnnotatedMethod_CallsExternalMethod_FiresCGC002()
    {
        var source = """
            using System;
            using MustNotAllocate;

            class C
            {
                [MustNotAllocate]
                void Caller() { Console.WriteLine("hi"); }
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.ExternalBoundary)
            .WithLocation(7, 21)
            .WithArguments("Caller", "MustNotAllocateAttribute", "WriteLine");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test tests/MustNotAllocate.Tests/ --filter "FullyQualifiedName~CGC002"`
Expected: PASS (the external-classification code from Task 6 already handles this).

- [ ] **Step 3: Commit**

```bash
git add tests/MustNotAllocate.Tests/CGC002_ExternalBoundaryTests.cs
git commit -m "test: add CGC002 external call boundary test"
```

---

## Task 8: CGC003 — object creation sink (TDD)

**Files:**
- Create: `src/MustNotAllocate/Sinks/ObjectCreationSink.cs`
- Modify: `src/MustNotAllocate/MustNotAllocateAnalyzer.cs`
- Modify: `src/CallgraphClosure.Core/CallgraphClosureAnalyzer.cs` (add sink dispatch + ctor boundary)
- Create: `tests/MustNotAllocate.Tests/CGC003_ObjectCreationTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/MustNotAllocate.Tests/CGC003_ObjectCreationTests.cs`:

```csharp
using System.Threading.Tasks;
using CallgraphClosure.Core;
using Xunit;

namespace MustNotAllocate.Tests;

public class CGC003_ObjectCreationTests
{
    [Fact]
    public async Task AnnotatedMethod_CreatesSourceObject_FiresCGC003AndCGC001()
    {
        var source = """
            using MustNotAllocate;

            class Foo { public Foo() {} }

            class C
            {
                [MustNotAllocate]
                void Caller() { var x = new Foo(); }
            }
            """;

        var sink = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SinkHit)
            .WithLocation(8, 29)
            .WithArguments("Caller", "MustNotAllocateAttribute", "object");

        var ctorEdge = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SourceBoundary)
            .WithLocation(8, 29)
            .WithArguments("Caller", "MustNotAllocateAttribute", "Foo");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, sink, ctorEdge);
    }

    [Fact]
    public async Task AnnotatedMethod_CreatesExternalObject_FiresCGC003AndCGC002()
    {
        var source = """
            using System.Text;
            using MustNotAllocate;

            class C
            {
                [MustNotAllocate]
                void Caller() { var x = new StringBuilder(); }
            }
            """;

        var sink = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SinkHit)
            .WithLocation(7, 29)
            .WithArguments("Caller", "MustNotAllocateAttribute", "object");

        var ctorEdge = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.ExternalBoundary)
            .WithLocation(7, 29)
            .WithArguments("Caller", "MustNotAllocateAttribute", "StringBuilder");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, sink, ctorEdge);
    }

    [Fact]
    public async Task AnnotatedMethod_CreatesStruct_DoesNotFireCGC003()
    {
        // Structs are stack-allocated; no CGC003. But the ctor still counts as a call
        // boundary if unannotated — so we expect CGC001 only.
        var source = """
            using MustNotAllocate;

            struct Point { public Point(int x) {} }

            class C
            {
                [MustNotAllocate]
                void Caller() { var p = new Point(1); }
            }
            """;

        var ctorEdge = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SourceBoundary)
            .WithLocation(8, 29)
            .WithArguments("Caller", "MustNotAllocateAttribute", "Point");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, ctorEdge);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MustNotAllocate.Tests/ --filter "FullyQualifiedName~CGC003_ObjectCreation"`
Expected: All three fail. The first two fail because CGC003 isn't produced (no sink wired yet) and also because `IObjectCreationOperation` isn't being treated as a boundary call in `VisitOp`. The third fails for the ctor boundary reason only.

- [ ] **Step 3: Write the ObjectCreationSink**

Create `src/MustNotAllocate/Sinks/ObjectCreationSink.cs`:

```csharp
using CallgraphClosure.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace MustNotAllocate.Sinks;

public sealed class ObjectCreationSink : ISink
{
    public string? Match(IOperation op)
    {
        if (op is not IObjectCreationOperation oc) return null;
        // Struct construction is stack allocation, not a heap allocation.
        if (oc.Type is null || oc.Type.IsValueType) return null;
        return "object";
    }
}
```

- [ ] **Step 4: Wire the sink into the analyzer**

Overwrite `src/MustNotAllocate/MustNotAllocateAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using CallgraphClosure.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using MustNotAllocate.Sinks;

namespace MustNotAllocate;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MustNotAllocateAnalyzer : CallgraphClosureAnalyzer
{
    public MustNotAllocateAnalyzer() : base(new Config(
        AttributeFullName: "MustNotAllocate.MustNotAllocateAttribute",
        Direction: PropagationDirection.Downward,
        Sinks: ImmutableArray.Create<ISink>(
            new ObjectCreationSink()))) { }
}
```

- [ ] **Step 5: Extend the core to fire sink diagnostics and treat ctors as boundaries**

Overwrite the `VisitOp` method in `src/CallgraphClosure.Core/CallgraphClosureAnalyzer.cs` (replace the existing `VisitOp`):

```csharp
    private void VisitOp(
        IOperation op,
        IMethodSymbol caller,
        INamedTypeSymbol attrSym,
        Compilation compilation,
        OperationBlockAnalysisContext b)
    {
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

        var isExternal = !SymbolEqualityComparer.Default.Equals(
            original.ContainingAssembly, compilation.Assembly);

        var descriptor = isExternal
            ? Diagnostics.ExternalBoundary
            : Diagnostics.SourceBoundary;

        b.ReportDiagnostic(Diagnostic.Create(
            descriptor,
            op.Syntax.GetLocation(),
            caller.Name,
            attrSym.Name,
            original.Name));
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/MustNotAllocate.Tests/ --filter "FullyQualifiedName~CGC003_ObjectCreation"`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 7: Run the full suite**

Run: `dotnet test tests/MustNotAllocate.Tests/`
Expected: All tests pass. Verify the Task 6 and Task 7 tests still pass.

- [ ] **Step 8: Commit**

```bash
git add src/MustNotAllocate/Sinks/ObjectCreationSink.cs
git add src/MustNotAllocate/MustNotAllocateAnalyzer.cs
git add src/CallgraphClosure.Core/CallgraphClosureAnalyzer.cs
git add tests/MustNotAllocate.Tests/CGC003_ObjectCreationTests.cs
git commit -m "feat: add CGC003 object creation sink and ctor boundary"
```

---

## Task 9: CGC003 — array creation sink (TDD)

**Files:**
- Create: `src/MustNotAllocate/Sinks/ArrayCreationSink.cs`
- Modify: `src/MustNotAllocate/MustNotAllocateAnalyzer.cs`
- Create: `tests/MustNotAllocate.Tests/CGC003_ArrayCreationTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/MustNotAllocate.Tests/CGC003_ArrayCreationTests.cs`:

```csharp
using System.Threading.Tasks;
using CallgraphClosure.Core;
using Xunit;

namespace MustNotAllocate.Tests;

public class CGC003_ArrayCreationTests
{
    [Fact]
    public async Task AnnotatedMethod_CreatesArrayWithSize_FiresCGC003()
    {
        var source = """
            using MustNotAllocate;

            class C
            {
                [MustNotAllocate]
                void Caller() { var a = new int[10]; }
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SinkHit)
            .WithLocation(6, 29)
            .WithArguments("Caller", "MustNotAllocateAttribute", "array");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task AnnotatedMethod_CreatesArrayWithInitializer_FiresCGC003()
    {
        var source = """
            using MustNotAllocate;

            class C
            {
                [MustNotAllocate]
                void Caller() { var a = new int[] { 1, 2, 3 }; }
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SinkHit)
            .WithLocation(6, 29)
            .WithArguments("Caller", "MustNotAllocateAttribute", "array");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MustNotAllocate.Tests/ --filter "FullyQualifiedName~CGC003_ArrayCreation"`
Expected: Both fail — CGC003 not produced.

- [ ] **Step 3: Implement the sink**

Create `src/MustNotAllocate/Sinks/ArrayCreationSink.cs`:

```csharp
using CallgraphClosure.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace MustNotAllocate.Sinks;

public sealed class ArrayCreationSink : ISink
{
    public string? Match(IOperation op) =>
        op is IArrayCreationOperation ? "array" : null;
}
```

- [ ] **Step 4: Wire the sink into the analyzer**

Overwrite the `Sinks` value in `src/MustNotAllocate/MustNotAllocateAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using CallgraphClosure.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using MustNotAllocate.Sinks;

namespace MustNotAllocate;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MustNotAllocateAnalyzer : CallgraphClosureAnalyzer
{
    public MustNotAllocateAnalyzer() : base(new Config(
        AttributeFullName: "MustNotAllocate.MustNotAllocateAttribute",
        Direction: PropagationDirection.Downward,
        Sinks: ImmutableArray.Create<ISink>(
            new ObjectCreationSink(),
            new ArrayCreationSink()))) { }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/MustNotAllocate.Tests/ --filter "FullyQualifiedName~CGC003_ArrayCreation"`
Expected: `Passed! - Failed: 0, Passed: 2`

- [ ] **Step 6: Run the full suite**

Run: `dotnet test tests/MustNotAllocate.Tests/`
Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/MustNotAllocate/Sinks/ArrayCreationSink.cs
git add src/MustNotAllocate/MustNotAllocateAnalyzer.cs
git add tests/MustNotAllocate.Tests/CGC003_ArrayCreationTests.cs
git commit -m "feat: add CGC003 array creation sink"
```

---

## Task 10: CGC003 — boxing conversion sink (TDD)

**Files:**
- Create: `src/MustNotAllocate/Sinks/BoxingConversionSink.cs`
- Modify: `src/MustNotAllocate/MustNotAllocateAnalyzer.cs`
- Create: `tests/MustNotAllocate.Tests/CGC003_BoxingTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/MustNotAllocate.Tests/CGC003_BoxingTests.cs`:

```csharp
using System.Threading.Tasks;
using CallgraphClosure.Core;
using Xunit;

namespace MustNotAllocate.Tests;

public class CGC003_BoxingTests
{
    [Fact]
    public async Task AnnotatedMethod_ImplicitBoxing_FiresCGC003()
    {
        var source = """
            using MustNotAllocate;

            class C
            {
                [MustNotAllocate]
                void Caller() { object o = 42; }
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SinkHit)
            .WithLocation(6, 32)
            .WithArguments("Caller", "MustNotAllocateAttribute", "boxing");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task AnnotatedMethod_ExplicitBoxing_FiresCGC003()
    {
        var source = """
            using MustNotAllocate;

            class C
            {
                [MustNotAllocate]
                void Caller() { object o = (object)42; }
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SinkHit)
            .WithLocation(6, 32)
            .WithArguments("Caller", "MustNotAllocateAttribute", "boxing");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task AnnotatedMethod_NoBoxing_FiresNothing()
    {
        var source = """
            using MustNotAllocate;

            class C
            {
                [MustNotAllocate]
                void Caller() { int x = 42; }
            }
            """;

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source);
    }
}
```

- [ ] **Step 2: Run the tests to verify the first two fail**

Run: `dotnet test tests/MustNotAllocate.Tests/ --filter "FullyQualifiedName~CGC003_Boxing"`
Expected: First two FAIL, third PASSES.

- [ ] **Step 3: Implement the sink**

Create `src/MustNotAllocate/Sinks/BoxingConversionSink.cs`:

```csharp
using CallgraphClosure.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;

namespace MustNotAllocate.Sinks;

public sealed class BoxingConversionSink : ISink
{
    public string? Match(IOperation op)
    {
        if (op is not IConversionOperation conv) return null;

        // IConversionOperation.Conversion is CommonConversion which lacks IsBoxing.
        // Use the C#-specific extension that returns a Microsoft.CodeAnalysis.CSharp.Conversion.
        var csharpConversion = CSharpExtensions.GetConversion(conv);
        return csharpConversion.IsBoxing ? "boxing" : null;
    }
}
```

- [ ] **Step 4: Wire the sink into the analyzer**

Overwrite `src/MustNotAllocate/MustNotAllocateAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using CallgraphClosure.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using MustNotAllocate.Sinks;

namespace MustNotAllocate;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MustNotAllocateAnalyzer : CallgraphClosureAnalyzer
{
    public MustNotAllocateAnalyzer() : base(new Config(
        AttributeFullName: "MustNotAllocate.MustNotAllocateAttribute",
        Direction: PropagationDirection.Downward,
        Sinks: ImmutableArray.Create<ISink>(
            new ObjectCreationSink(),
            new ArrayCreationSink(),
            new BoxingConversionSink()))) { }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/MustNotAllocate.Tests/ --filter "FullyQualifiedName~CGC003_Boxing"`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 6: Run the full suite**

Run: `dotnet test tests/MustNotAllocate.Tests/`
Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/MustNotAllocate/Sinks/BoxingConversionSink.cs
git add src/MustNotAllocate/MustNotAllocateAnalyzer.cs
git add tests/MustNotAllocate.Tests/CGC003_BoxingTests.cs
git commit -m "feat: add CGC003 boxing conversion sink"
```

---

## Task 11: Generic OriginalDefinition unwrap (test-only)

Locks in the semantics that `OriginalDefinition` is used for attribute lookup, so `Foo<int>` inherits the annotation of `Foo<T>`.

**Files:**
- Create: `tests/MustNotAllocate.Tests/GenericUnwrapTests.cs`

- [ ] **Step 1: Write the tests**

Create `tests/MustNotAllocate.Tests/GenericUnwrapTests.cs`:

```csharp
using System.Threading.Tasks;
using CallgraphClosure.Core;
using Xunit;

namespace MustNotAllocate.Tests;

public class GenericUnwrapTests
{
    [Fact]
    public async Task AnnotatedGenericCallee_ConstructedForm_DoesNotFireBoundary()
    {
        var source = """
            using MustNotAllocate;

            class C
            {
                [MustNotAllocate]
                void Caller() { Callee<int>(); }

                [MustNotAllocate]
                void Callee<T>() { }
            }
            """;

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task UnannotatedGenericCallee_ConstructedForm_FiresOnceAsCGC001()
    {
        var source = """
            using MustNotAllocate;

            class C
            {
                [MustNotAllocate]
                void Caller() { Callee<int>(); }

                void Callee<T>() { }
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SourceBoundary)
            .WithLocation(6, 21)
            .WithArguments("Caller", "MustNotAllocateAttribute", "Callee");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test tests/MustNotAllocate.Tests/ --filter "FullyQualifiedName~GenericUnwrap"`
Expected: Both PASS (the `OriginalDefinition` unwrap in `VisitOp` already handles this correctly).

- [ ] **Step 3: Commit**

```bash
git add tests/MustNotAllocate.Tests/GenericUnwrapTests.cs
git commit -m "test: add generic OriginalDefinition unwrap tests"
```

---

## Task 12: Cascading annotation (test-only)

Confirms that emergent propagation works: annotating a method shifts diagnostics to its callees.

**Files:**
- Create: `tests/MustNotAllocate.Tests/CascadingTests.cs`

- [ ] **Step 1: Write the tests**

Create `tests/MustNotAllocate.Tests/CascadingTests.cs`:

```csharp
using System.Threading.Tasks;
using CallgraphClosure.Core;
using Xunit;

namespace MustNotAllocate.Tests;

public class CascadingTests
{
    [Fact]
    public async Task BeforeAnnotatingMiddle_DiagnosticOnOuterCall()
    {
        var source = """
            using MustNotAllocate;

            class C
            {
                [MustNotAllocate]
                void A() { B(); }

                void B() { C_(); }

                void C_() { }
            }
            """;

        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SourceBoundary)
            .WithLocation(6, 16)
            .WithArguments("A", "MustNotAllocateAttribute", "B");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task AfterAnnotatingMiddle_DiagnosticShiftsToInnerCall()
    {
        var source = """
            using MustNotAllocate;

            class C
            {
                [MustNotAllocate]
                void A() { B(); }

                [MustNotAllocate]
                void B() { C_(); }

                void C_() { }
            }
            """;

        // A→B is now fine (both annotated); B→C_ is now the violation.
        var expected = CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .Diagnostic(Diagnostics.SourceBoundary)
            .WithLocation(9, 16)
            .WithArguments("B", "MustNotAllocateAttribute", "C_");

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source, expected);
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test tests/MustNotAllocate.Tests/ --filter "FullyQualifiedName~Cascading"`
Expected: Both PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/MustNotAllocate.Tests/CascadingTests.cs
git commit -m "test: add cascading annotation propagation tests"
```

---

## Task 13: No-attribute-reference silent no-op (test-only)

Verifies the analyzer silently bails when the attribute type isn't in the compilation. Uses a different user-defined attribute with a confusingly similar name to rule out string-match accidents.

**Files:**
- Create: `tests/MustNotAllocate.Tests/NoAttributeReferenceTests.cs`

- [ ] **Step 1: Write the test**

Create `tests/MustNotAllocate.Tests/NoAttributeReferenceTests.cs`:

```csharp
using System.Threading.Tasks;
using Xunit;

namespace MustNotAllocate.Tests;

public class NoAttributeReferenceTests
{
    [Fact]
    public async Task UserDefinedLikeNamedAttribute_IsNotMatched()
    {
        // User defined their own [MustNotAllocate] in the wrong namespace.
        // The analyzer looks for MustNotAllocate.MustNotAllocateAttribute by FQN,
        // so this source should produce no diagnostics.
        var source = """
            namespace Other
            {
                class MustNotAllocateAttribute : System.Attribute { }

                class C
                {
                    [MustNotAllocate]
                    void Caller() { Callee(); }

                    void Callee() { }
                }
            }
            """;

        await CSharpAnalyzerVerifier<MustNotAllocateAnalyzer>
            .VerifyAnalyzerAsync(source);
    }
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test tests/MustNotAllocate.Tests/ --filter "FullyQualifiedName~NoAttributeReference"`
Expected: PASS. Note: the real `MustNotAllocate.MustNotAllocateAttribute` is still present (via the test-harness's `AdditionalReferences`), but this test uses an `Other.MustNotAllocateAttribute` so there is no match on the real FQN.

- [ ] **Step 3: Commit**

```bash
git add tests/MustNotAllocate.Tests/NoAttributeReferenceTests.cs
git commit -m "test: verify analyzer ignores same-named attributes with different FQN"
```

---

## Task 14: Sample project with intentional violations

A manual-verification app demonstrating the analyzer in action — used for IDE-squiggle screenshots in the writeup.

**Files:**
- Create: `src/MustNotAllocate.Sample/MustNotAllocate.Sample.csproj`
- Create: `src/MustNotAllocate.Sample/Program.cs`

- [ ] **Step 1: Create the project**

Run:
```bash
dotnet new console -o src/MustNotAllocate.Sample -f net8.0
rm src/MustNotAllocate.Sample/Program.cs
dotnet sln add src/MustNotAllocate.Sample/MustNotAllocate.Sample.csproj
```

- [ ] **Step 2: Configure the .csproj to consume the analyzer and the attribute**

Overwrite `src/MustNotAllocate.Sample/MustNotAllocate.Sample.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\MustNotAllocate\MustNotAllocate.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="true" />
    <ProjectReference Include="..\CallgraphClosure.Core\CallgraphClosure.Core.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

`TreatWarningsAsErrors` is deliberately `false` here — the sample is meant to emit warnings. The `CallgraphClosure.Core` reference has `ReferenceOutputAssembly=false` because the sample doesn't use Core's types directly, it just needs Core's assembly present alongside the analyzer DLL.

- [ ] **Step 3: Write the sample program**

Create `src/MustNotAllocate.Sample/Program.cs`:

```csharp
using MustNotAllocate;

// Toy "audio tick" loop — two intentional violations for the writeup.

while (true)
{
    Tick(42);
}

[MustNotAllocate]
static void Tick(int sample)
{
    // Violation 1: CGC002 (external boundary) + unrelated, Console.WriteLine is external.
    System.Console.WriteLine(sample);

    // Violation 2: CGC003 (array allocation).
    var scratch = new int[16];
    _ = scratch;
}
```

- [ ] **Step 4: Build and observe the diagnostics**

Run: `dotnet build src/MustNotAllocate.Sample/`
Expected: Build succeeds, and the output contains at least:
- `CGC002` on the `System.Console.WriteLine(sample)` line
- `CGC003` on the `new int[16]` line

- [ ] **Step 5: Commit**

```bash
git add src/MustNotAllocate.Sample/
git commit -m "feat(sample): add toy hot-loop demo with intentional violations"
```

---

## Task 15: Final full-suite run and milestone commit

**Files:**
- (none — verification only)

- [ ] **Step 1: Run the full test suite from repo root**

Run: `dotnet test`
Expected: All tests in `MustNotAllocate.Tests` pass. Total count should be around 15 tests:
- SmokeTests: 1
- CGC001 source boundary: 3
- CGC002 external boundary: 1
- CGC003 object creation: 3
- CGC003 array creation: 2
- CGC003 boxing: 3
- Generic unwrap: 2
- Cascading: 2
- No-attribute-reference: 1

Approximately 18 tests total. Confirm `Passed: N, Failed: 0`.

- [ ] **Step 2: Build the full solution**

Run: `dotnet build CallgraphClosure.sln`
Expected: All projects build; the sample emits the expected CGC warnings.

- [ ] **Step 3: Tag the milestone**

Run:
```bash
git tag -a m1-complete -m "Milestone 1: Roslyn analyzer for [MustNotAllocate] direct-call closure"
```

Expected: `git tag --list` shows `m1-complete`.
