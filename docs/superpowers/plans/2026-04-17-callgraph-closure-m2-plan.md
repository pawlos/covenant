# Callgraph-Closure Lint M2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Cecil-based IL post-pass that walks transitively across assembly boundaries, upgrading M1's CGC002 warnings to concrete CGC003/CGC001-equivalents where the realized callgraph shows sinks.

**Architecture:** Three net10.0 projects mirror M1's layer split — `CallgraphClosure.ILCheck.Core` (reusable IL walker over Cecil) + `MustNotAllocate.ILCheck` (property-specific sinks) + `CallgraphClosure.ILCheck.Cli` (console entry). Tests compile C# fixtures to temp DLLs at runtime, then run the walker over them.

**Tech Stack:** Mono.Cecil 0.11.5, Microsoft.CodeAnalysis.CSharp 4.8.0 (reused from M1 for fixture compilation), xUnit 2.4.2 (pinned — see `known_issues.md`), net10.0 for non-analyzer projects, netstandard2.0 unchanged for M1 analyzers.

**Reference spec:** `docs/superpowers/specs/2026-04-17-callgraph-closure-m2-design.md`

**Note:** The project isn't in a worktree (brand new M2 work on top of M1). Continue on `master` branch.

---

## Task 1: Upgrade M1 non-analyzer projects to net10.0

Pre-work cleanup before touching M2 code. Analyzer projects (`CallgraphClosure.Core`, `MustNotAllocate`) stay on netstandard2.0 — that's a Roslyn SDK requirement.

**Files:**
- Modify: `src/MustNotAllocate.Sample/MustNotAllocate.Sample.csproj`
- Modify: `tests/MustNotAllocate.Tests/MustNotAllocate.Tests.csproj`
- Modify: `tests/MustNotAllocate.Tests/CSharpAnalyzerVerifier.cs`

- [ ] **Step 1: Check baseline**

Run:
```bash
dotnet test
dotnet build CallgraphClosure.sln
```

Expected: 18 tests pass, solution builds with 2 CGC warnings in the sample. (Baseline from M1.)

- [ ] **Step 2: Update the sample TFM**

In `src/MustNotAllocate.Sample/MustNotAllocate.Sample.csproj`, change `<TargetFramework>net8.0</TargetFramework>` to `<TargetFramework>net10.0</TargetFramework>`. The rest of the file is unchanged.

- [ ] **Step 3: Update the test project TFM**

In `tests/MustNotAllocate.Tests/MustNotAllocate.Tests.csproj`, change `<TargetFramework>net8.0</TargetFramework>` to `<TargetFramework>net10.0</TargetFramework>`.

- [ ] **Step 4: Update the analyzer-testing reference assemblies constant**

In `tests/MustNotAllocate.Tests/CSharpAnalyzerVerifier.cs`, find the line:

```csharp
ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
```

Replace with:

```csharp
ReferenceAssemblies = ReferenceAssemblies.Net.Net100;
```

Note: if `Microsoft.CodeAnalysis.Testing` 1.1.2 doesn't expose `Net100` as a constant, construct one manually:

```csharp
ReferenceAssemblies = new ReferenceAssemblies(
    "net10.0",
    new PackageIdentity("Microsoft.NETCore.App.Ref", "10.0.0"),
    System.IO.Path.Combine("ref", "net10.0"));
```

If neither works, report the actual compilation error as DONE_WITH_CONCERNS and the controller will decide.

- [ ] **Step 5: Verify tests still pass**

Run: `dotnet test`
Expected: 18 tests pass, all green.

- [ ] **Step 6: Verify sample still builds with the 2 expected warnings**

Run: `dotnet build CallgraphClosure.sln 2>&1 | grep CGC`
Expected: one `CGC002` warning on `Console.WriteLine`, one `CGC003` warning on `new int[16]`.

- [ ] **Step 7: Commit**

```bash
git add src/MustNotAllocate.Sample/MustNotAllocate.Sample.csproj
git add tests/MustNotAllocate.Tests/MustNotAllocate.Tests.csproj
git add tests/MustNotAllocate.Tests/CSharpAnalyzerVerifier.cs
git commit -m "chore: upgrade non-analyzer projects from net8.0 to net10.0"
```

---

## Task 2: CallgraphClosure.ILCheck.Core project scaffolding

Create the reusable core project for the IL pass. Analogous to M1's `CallgraphClosure.Core` but built over Cecil, not Roslyn. No dependency on the M1 Roslyn core — IL pass is independent.

**Files:**
- Create: `src/CallgraphClosure.ILCheck.Core/CallgraphClosure.ILCheck.Core.csproj`
- Create: `src/CallgraphClosure.ILCheck.Core/PropagationDirection.cs`
- Create: `src/CallgraphClosure.ILCheck.Core/DiagnosticIds.cs`
- Create: `src/CallgraphClosure.ILCheck.Core/Diagnostic.cs`
- Create: `src/CallgraphClosure.ILCheck.Core/IIlSink.cs`

- [ ] **Step 1: Create the project**

Run:
```bash
dotnet new classlib -o src/CallgraphClosure.ILCheck.Core -f net10.0
rm src/CallgraphClosure.ILCheck.Core/Class1.cs
dotnet sln add src/CallgraphClosure.ILCheck.Core/CallgraphClosure.ILCheck.Core.csproj
```

- [ ] **Step 2: Configure the .csproj**

Overwrite `src/CallgraphClosure.ILCheck.Core/CallgraphClosure.ILCheck.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Mono.Cecil" Version="0.11.5" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write PropagationDirection.cs**

Create `src/CallgraphClosure.ILCheck.Core/PropagationDirection.cs`:

```csharp
namespace CallgraphClosure.ILCheck.Core;

public enum PropagationDirection
{
    Downward,
    // Upward reserved for future work.
}
```

- [ ] **Step 4: Write DiagnosticIds.cs**

Create `src/CallgraphClosure.ILCheck.Core/DiagnosticIds.cs`:

```csharp
namespace CallgraphClosure.ILCheck.Core;

public static class DiagnosticIds
{
    public const string SourceBoundary = "CGC001";
    public const string ExternalBoundary = "CGC002";
    public const string SinkHit = "CGC003";
}
```

- [ ] **Step 5: Write Diagnostic.cs**

Create `src/CallgraphClosure.ILCheck.Core/Diagnostic.cs`:

```csharp
using System.Collections.Immutable;
using Mono.Cecil;

namespace CallgraphClosure.ILCheck.Core;

public sealed record Diagnostic(
    string Id,
    string PropertyName,
    MethodDefinition AnnotatedCaller,
    ImmutableArray<MethodReference> Chain,
    string? SinkLabel,
    MethodReference? UnresolvedTarget);
```

- [ ] **Step 6: Write IIlSink.cs**

Create `src/CallgraphClosure.ILCheck.Core/IIlSink.cs`:

```csharp
using Mono.Cecil.Cil;

namespace CallgraphClosure.ILCheck.Core;

public interface IIlSink
{
    // Returns a label (e.g. "object", "array", "boxing") if this sink matches the instruction,
    // otherwise null.
    string? Match(Instruction instruction);
}
```

- [ ] **Step 7: Verify it builds**

Run: `dotnet build src/CallgraphClosure.ILCheck.Core/`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 8: Commit**

```bash
git add src/CallgraphClosure.ILCheck.Core/
git commit -m "feat(ilcheck): add core scaffolding - ids, diagnostic record, IIlSink"
```

---

## Task 3: AssemblyResolver for Cecil

Cecil needs to resolve cross-assembly method calls. The resolver uses a list of search directories plus a runtime framework path.

**Files:**
- Create: `src/CallgraphClosure.ILCheck.Core/AssemblyResolver.cs`

- [ ] **Step 1: Write the resolver**

Create `src/CallgraphClosure.ILCheck.Core/AssemblyResolver.cs`:

```csharp
using System;
using System.IO;
using Mono.Cecil;

namespace CallgraphClosure.ILCheck.Core;

public sealed class AssemblyResolver : BaseAssemblyResolver
{
    public AssemblyResolver(params string[] searchDirectories)
    {
        foreach (var dir in searchDirectories)
        {
            if (Directory.Exists(dir))
                AddSearchDirectory(dir);
        }

        // Always include the current runtime's base directory as a fallback so BCL
        // reference resolution works out of the box for tests and sample walks.
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (!string.IsNullOrEmpty(runtimeDir))
            AddSearchDirectory(runtimeDir);
    }

    public static AssemblyResolver ForAssemblyPath(string assemblyPath)
    {
        var assemblyDir = Path.GetDirectoryName(Path.GetFullPath(assemblyPath)) ?? ".";
        return new AssemblyResolver(assemblyDir);
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/CallgraphClosure.ILCheck.Core/`
Expected: 0/0.

- [ ] **Step 3: Commit**

```bash
git add src/CallgraphClosure.ILCheck.Core/AssemblyResolver.cs
git commit -m "feat(ilcheck): add Cecil assembly resolver with runtime-base fallback"
```

---

## Task 4: MustNotAllocate.ILCheck project with three sinks and config

Concrete property module for `[MustNotAllocate]` — the IL-level version of M1's `MustNotAllocate` project. Three sinks (newobj-ref, newarr, box) plus an `Analyzer` class that exposes the configured sink list and attribute FQN.

**Files:**
- Create: `src/MustNotAllocate.ILCheck/MustNotAllocate.ILCheck.csproj`
- Create: `src/MustNotAllocate.ILCheck/Sinks/NewObjSink.cs`
- Create: `src/MustNotAllocate.ILCheck/Sinks/NewArrSink.cs`
- Create: `src/MustNotAllocate.ILCheck/Sinks/BoxSink.cs`
- Create: `src/MustNotAllocate.ILCheck/MustNotAllocateIlAnalyzer.cs`

- [ ] **Step 1: Create the project**

Run:
```bash
dotnet new classlib -o src/MustNotAllocate.ILCheck -f net10.0
rm src/MustNotAllocate.ILCheck/Class1.cs
dotnet sln add src/MustNotAllocate.ILCheck/MustNotAllocate.ILCheck.csproj
```

- [ ] **Step 2: Configure the .csproj**

Overwrite `src/MustNotAllocate.ILCheck/MustNotAllocate.ILCheck.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CallgraphClosure.ILCheck.Core\CallgraphClosure.ILCheck.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create the Sinks directory and NewObjSink**

Run: `mkdir -p src/MustNotAllocate.ILCheck/Sinks`

Create `src/MustNotAllocate.ILCheck/Sinks/NewObjSink.cs`:

```csharp
using CallgraphClosure.ILCheck.Core;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MustNotAllocate.ILCheck.Sinks;

public sealed class NewObjSink : IIlSink
{
    public string? Match(Instruction instruction)
    {
        if (instruction.OpCode != OpCodes.Newobj) return null;
        if (instruction.Operand is not MethodReference ctor) return null;

        // Struct construction via newobj on a value type does not heap-allocate.
        // IsValueType on the declaring TypeReference covers this.
        if (ctor.DeclaringType.IsValueType) return null;

        return "object";
    }
}
```

- [ ] **Step 4: Write NewArrSink**

Create `src/MustNotAllocate.ILCheck/Sinks/NewArrSink.cs`:

```csharp
using CallgraphClosure.ILCheck.Core;
using Mono.Cecil.Cil;

namespace MustNotAllocate.ILCheck.Sinks;

public sealed class NewArrSink : IIlSink
{
    public string? Match(Instruction instruction) =>
        instruction.OpCode == OpCodes.Newarr ? "array" : null;
}
```

- [ ] **Step 5: Write BoxSink**

Create `src/MustNotAllocate.ILCheck/Sinks/BoxSink.cs`:

```csharp
using CallgraphClosure.ILCheck.Core;
using Mono.Cecil.Cil;

namespace MustNotAllocate.ILCheck.Sinks;

public sealed class BoxSink : IIlSink
{
    public string? Match(Instruction instruction) =>
        instruction.OpCode == OpCodes.Box ? "boxing" : null;
}
```

- [ ] **Step 6: Write the analyzer config binding**

Create `src/MustNotAllocate.ILCheck/MustNotAllocateIlAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using CallgraphClosure.ILCheck.Core;
using MustNotAllocate.ILCheck.Sinks;

namespace MustNotAllocate.ILCheck;

public static class MustNotAllocateIlAnalyzer
{
    public const string AttributeFullName = "MustNotAllocate.MustNotAllocateAttribute";

    public static ImmutableArray<IIlSink> Sinks { get; } =
        ImmutableArray.Create<IIlSink>(
            new NewObjSink(),
            new NewArrSink(),
            new BoxSink());
}
```

- [ ] **Step 7: Verify build**

Run: `dotnet build src/MustNotAllocate.ILCheck/`
Expected: 0/0.

- [ ] **Step 8: Commit**

```bash
git add src/MustNotAllocate.ILCheck/
git commit -m "feat(must-not-allocate-ilcheck): add IL sinks (newobj/newarr/box) and analyzer binding"
```

---

## Task 5: Test project scaffolding with CompileFixture helper

Sets up the xUnit test project. The key helper is `CompileFixture` — compiles a C# source string in-memory to a DLL on disk so Cecil can read it.

**Files:**
- Create: `tests/MustNotAllocate.ILCheck.Tests/MustNotAllocate.ILCheck.Tests.csproj`
- Create: `tests/MustNotAllocate.ILCheck.Tests/CompileFixture.cs`
- Create: `tests/MustNotAllocate.ILCheck.Tests/SmokeTests.cs`

- [ ] **Step 1: Create the project**

Run:
```bash
dotnet new xunit -o tests/MustNotAllocate.ILCheck.Tests -f net10.0
rm tests/MustNotAllocate.ILCheck.Tests/UnitTest1.cs
dotnet sln add tests/MustNotAllocate.ILCheck.Tests/MustNotAllocate.ILCheck.Tests.csproj
```

- [ ] **Step 2: Configure the .csproj**

Overwrite `tests/MustNotAllocate.ILCheck.Tests/MustNotAllocate.ILCheck.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.9.0" />
    <PackageReference Include="xunit" Version="2.4.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.7" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\CallgraphClosure.ILCheck.Core\CallgraphClosure.ILCheck.Core.csproj" />
    <ProjectReference Include="..\..\src\MustNotAllocate.ILCheck\MustNotAllocate.ILCheck.csproj" />
    <ProjectReference Include="..\..\src\MustNotAllocate\MustNotAllocate.csproj" />
  </ItemGroup>
</Project>
```

The `MustNotAllocate` ProjectReference is for the `[MustNotAllocate]` attribute type — fixtures need to reference it so `[MustNotAllocate]` resolves during fixture compilation.

- [ ] **Step 3: Write CompileFixture**

Create `tests/MustNotAllocate.ILCheck.Tests/CompileFixture.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MustNotAllocate.ILCheck.Tests;

// Compiles a C# source string to a DLL in a fresh temp directory,
// copying MustNotAllocate.dll alongside so Cecil can resolve the attribute.
public static class CompileFixture
{
    public static string Emit(string source, string assemblyName = "Fixture")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "cgc-il-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var outputPath = Path.Combine(tempDir, assemblyName + ".dll");

        var references = GetStandardReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var result = compilation.Emit(outputPath);
        if (!result.Success)
        {
            var errors = string.Join(
                Environment.NewLine,
                result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException(
                "Fixture compilation failed:" + Environment.NewLine + errors);
        }

        // Copy MustNotAllocate.dll next to the fixture so Cecil's resolver finds it.
        var mustNotAllocateDllPath = typeof(global::MustNotAllocate.MustNotAllocateAttribute)
            .Assembly.Location;
        File.Copy(
            mustNotAllocateDllPath,
            Path.Combine(tempDir, Path.GetFileName(mustNotAllocateDllPath)),
            overwrite: true);

        return outputPath;
    }

    private static IEnumerable<MetadataReference> GetStandardReferences()
    {
        // Reference the same assemblies the test host loaded — covers BCL plus our own projects.
        var trustedPlatformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?? throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES not found");

        foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator))
        {
            if (File.Exists(path))
                yield return MetadataReference.CreateFromFile(path);
        }

        // Make sure MustNotAllocate is referenced so fixtures can use [MustNotAllocate].
        yield return MetadataReference.CreateFromFile(
            typeof(global::MustNotAllocate.MustNotAllocateAttribute).Assembly.Location);
    }
}
```

- [ ] **Step 4: Write a smoke test**

Create `tests/MustNotAllocate.ILCheck.Tests/SmokeTests.cs`:

```csharp
using System.IO;
using Xunit;

namespace MustNotAllocate.ILCheck.Tests;

public class SmokeTests
{
    [Fact]
    public void CompileFixture_ProducesReadableDll()
    {
        var source = """
            using MustNotAllocate;

            public class C
            {
                [MustNotAllocate]
                public void Caller() { }
            }
            """;

        var dllPath = CompileFixture.Emit(source);

        Assert.True(File.Exists(dllPath), $"Expected DLL at {dllPath}");
        Assert.True(new FileInfo(dllPath).Length > 0, "Expected non-empty DLL");
    }
}
```

- [ ] **Step 5: Run the smoke test**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/`
Expected: `Passed! - Failed: 0, Passed: 1, Total: 1`.

If compilation fails with "TRUSTED_PLATFORM_ASSEMBLIES not found," this is because the test host doesn't expose this AppContext key on net10 (it does on net8 and earlier). Fallback: iterate over `AppDomain.CurrentDomain.GetAssemblies()` and reference each `Assembly.Location`. Report DONE_WITH_CONCERNS if you have to switch strategies.

- [ ] **Step 6: Commit**

```bash
git add tests/MustNotAllocate.ILCheck.Tests/
git commit -m "test(ilcheck): add test project with CompileFixture and smoke test"
```

---

## Task 6: ClosureWalker skeleton (no-op)

Create the `ClosureWalker` class with the API surface but no logic yet. Tasks 7-14 fill it in test-first.

**Files:**
- Create: `src/CallgraphClosure.ILCheck.Core/ClosureWalker.cs`

- [ ] **Step 1: Write the skeleton**

Create `src/CallgraphClosure.ILCheck.Core/ClosureWalker.cs`:

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using Mono.Cecil;

namespace CallgraphClosure.ILCheck.Core;

public sealed class ClosureWalker
{
    private readonly string _attributeFullName;
    private readonly ImmutableArray<IIlSink> _sinks;
    private readonly string _propertyName;

    public ClosureWalker(
        string attributeFullName,
        ImmutableArray<IIlSink> sinks,
        string propertyName)
    {
        _attributeFullName = attributeFullName;
        _sinks = sinks;
        _propertyName = propertyName;
    }

    public ImmutableArray<Diagnostic> Analyze(AssemblyDefinition assembly)
    {
        // Implemented in later tasks.
        return ImmutableArray<Diagnostic>.Empty;
    }

    private bool HasPropagatingAttribute(MethodDefinition method)
    {
        foreach (var attr in method.CustomAttributes)
        {
            if (attr.AttributeType.FullName == _attributeFullName)
                return true;
        }
        return false;
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/CallgraphClosure.ILCheck.Core/`
Expected: 0/0.

- [ ] **Step 3: Commit**

```bash
git add src/CallgraphClosure.ILCheck.Core/ClosureWalker.cs
git commit -m "feat(ilcheck): add ClosureWalker skeleton with no-op Analyze"
```

---

## Task 7: Direct array sink detection (TDD)

First substantive TDD task. Walker detects `newarr` in an annotated method's body and reports CGC003.

**Files:**
- Create: `tests/MustNotAllocate.ILCheck.Tests/DirectSinkTests.cs`
- Modify: `src/CallgraphClosure.ILCheck.Core/ClosureWalker.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/MustNotAllocate.ILCheck.Tests/DirectSinkTests.cs`:

```csharp
using System.Collections.Immutable;
using System.Linq;
using CallgraphClosure.ILCheck.Core;
using MustNotAllocate.ILCheck;
using Mono.Cecil;
using Xunit;

namespace MustNotAllocate.ILCheck.Tests;

public class DirectSinkTests
{
    private static ClosureWalker BuildWalker() => new(
        MustNotAllocateIlAnalyzer.AttributeFullName,
        MustNotAllocateIlAnalyzer.Sinks,
        propertyName: "MustNotAllocate");

    [Fact]
    public void AnnotatedMethod_AllocatesArray_FiresCGC003()
    {
        var source = """
            using MustNotAllocate;

            public class C
            {
                [MustNotAllocate]
                public void Caller() { var a = new int[10]; }
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        var sinkHits = diagnostics
            .Where(d => d.Id == DiagnosticIds.SinkHit)
            .ToImmutableArray();

        Assert.Single(sinkHits);
        Assert.Equal("array", sinkHits[0].SinkLabel);
        Assert.Equal("Caller", sinkHits[0].AnnotatedCaller.Name);
    }
}
```

- [ ] **Step 2: Run the test — expect failure**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/ --filter "FullyQualifiedName~DirectSinkTests"`
Expected: FAIL (`Analyze` returns empty; `Single` throws).

- [ ] **Step 3: Implement direct sink walking**

Overwrite `src/CallgraphClosure.ILCheck.Core/ClosureWalker.cs`:

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using Mono.Cecil;

namespace CallgraphClosure.ILCheck.Core;

public sealed class ClosureWalker
{
    private readonly string _attributeFullName;
    private readonly ImmutableArray<IIlSink> _sinks;
    private readonly string _propertyName;

    public ClosureWalker(
        string attributeFullName,
        ImmutableArray<IIlSink> sinks,
        string propertyName)
    {
        _attributeFullName = attributeFullName;
        _sinks = sinks;
        _propertyName = propertyName;
    }

    public ImmutableArray<Diagnostic> Analyze(AssemblyDefinition assembly)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (var type in assembly.MainModule.Types)
        {
            foreach (var method in type.Methods)
            {
                if (!HasPropagatingAttribute(method)) continue;

                VisitMethodBody(
                    method,
                    annotatedCaller: method,
                    chain: ImmutableArray.Create<MethodReference>(method),
                    diagnostics);
            }
        }

        return diagnostics.ToImmutable();
    }

    private void VisitMethodBody(
        MethodDefinition method,
        MethodDefinition annotatedCaller,
        ImmutableArray<MethodReference> chain,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (method.Body is null) return;

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
        }
    }

    private bool HasPropagatingAttribute(MethodDefinition method)
    {
        foreach (var attr in method.CustomAttributes)
        {
            if (attr.AttributeType.FullName == _attributeFullName)
                return true;
        }
        return false;
    }
}
```

- [ ] **Step 4: Run the test — expect pass**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/ --filter "FullyQualifiedName~DirectSinkTests"`
Expected: PASS.

- [ ] **Step 5: Run full suite**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/`
Expected: 2 tests pass (smoke + direct-array).

- [ ] **Step 6: Commit**

```bash
git add src/CallgraphClosure.ILCheck.Core/ClosureWalker.cs
git add tests/MustNotAllocate.ILCheck.Tests/DirectSinkTests.cs
git commit -m "feat(ilcheck): detect direct newarr sinks in annotated methods"
```

---

## Task 8: Direct newobj and boxing sinks (TDD)

Add tests that `newobj` on a reference type and `box` on a value type both fire CGC003. The implementation from Task 7 already walks all sinks — these tests should pass immediately since NewObjSink and BoxSink are already wired. Tests lock in the behavior.

**Files:**
- Modify: `tests/MustNotAllocate.ILCheck.Tests/DirectSinkTests.cs`

- [ ] **Step 1: Add two tests**

Append to `tests/MustNotAllocate.ILCheck.Tests/DirectSinkTests.cs`, inside the class:

```csharp
    [Fact]
    public void AnnotatedMethod_CreatesReferenceObject_FiresCGC003Object()
    {
        var source = """
            using MustNotAllocate;

            public class Foo { public Foo() {} }

            public class C
            {
                [MustNotAllocate]
                public void Caller() { var x = new Foo(); }
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        var sinkHits = diagnostics.Where(d => d.Id == DiagnosticIds.SinkHit).ToImmutableArray();
        Assert.Single(sinkHits);
        Assert.Equal("object", sinkHits[0].SinkLabel);
    }

    [Fact]
    public void AnnotatedMethod_BoxesValue_FiresCGC003Boxing()
    {
        var source = """
            using MustNotAllocate;

            public class C
            {
                [MustNotAllocate]
                public void Caller() { object o = 42; }
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        var sinkHits = diagnostics.Where(d => d.Id == DiagnosticIds.SinkHit).ToImmutableArray();
        Assert.Single(sinkHits);
        Assert.Equal("boxing", sinkHits[0].SinkLabel);
    }

    [Fact]
    public void AnnotatedMethod_CreatesStruct_DoesNotFireCGC003()
    {
        var source = """
            using MustNotAllocate;

            public struct Point { public Point(int x) {} }

            public class C
            {
                [MustNotAllocate]
                public void Caller() { var p = new Point(1); }
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        var sinkHits = diagnostics.Where(d => d.Id == DiagnosticIds.SinkHit).ToImmutableArray();
        Assert.Empty(sinkHits);
    }
```

- [ ] **Step 2: Run the filtered tests**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/ --filter "FullyQualifiedName~DirectSinkTests"`
Expected: 4 tests pass (array + object + boxing + struct-negative).

If `AnnotatedMethod_BoxesValue_FiresCGC003Boxing` fails because the C# compiler optimized the boxing away (it sometimes does for `object o = 42;` assignment to an unused local), change the source to use the boxed value:

```csharp
    public void Caller() { object o = 42; System.GC.KeepAlive(o); }
```

and expect one sink hit (the box) PLUS the walk will see `GC.KeepAlive` as a cross-assembly call — ignore any CGC001/002 produced by that call for this test by filtering to `Id == DiagnosticIds.SinkHit` (which the test already does).

- [ ] **Step 3: Run full suite**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/`
Expected: 5 tests pass (1 smoke + 4 direct sinks).

- [ ] **Step 4: Commit**

```bash
git add tests/MustNotAllocate.ILCheck.Tests/DirectSinkTests.cs
git commit -m "test(ilcheck): add direct newobj, boxing, and struct-negative sink tests"
```

---

## Task 9: Call boundary diagnostic — CGC001 for same-assembly unannotated callee (TDD)

Extend `VisitMethodBody` to inspect `call` / `callvirt` / `newobj` instructions and emit CGC001 when the callee is in the same assembly and unannotated.

**Files:**
- Create: `tests/MustNotAllocate.ILCheck.Tests/SameAssemblyBoundaryTests.cs`
- Modify: `src/CallgraphClosure.ILCheck.Core/ClosureWalker.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/MustNotAllocate.ILCheck.Tests/SameAssemblyBoundaryTests.cs`:

```csharp
using System.Collections.Immutable;
using System.Linq;
using CallgraphClosure.ILCheck.Core;
using MustNotAllocate.ILCheck;
using Mono.Cecil;
using Xunit;

namespace MustNotAllocate.ILCheck.Tests;

public class SameAssemblyBoundaryTests
{
    private static ClosureWalker BuildWalker() => new(
        MustNotAllocateIlAnalyzer.AttributeFullName,
        MustNotAllocateIlAnalyzer.Sinks,
        propertyName: "MustNotAllocate");

    [Fact]
    public void AnnotatedMethod_CallsUnannotatedSameAssembly_FiresCGC001()
    {
        // Callee is empty so there are no transitive sinks. Only the boundary fires.
        var source = """
            using MustNotAllocate;

            public class C
            {
                [MustNotAllocate]
                public void Caller() { Callee(); }

                public void Callee() { }
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        var boundaryHits = diagnostics
            .Where(d => d.Id == DiagnosticIds.SourceBoundary)
            .ToImmutableArray();

        Assert.Single(boundaryHits);
        Assert.Equal("Callee", boundaryHits[0].Chain.Last().Name);
    }

    [Fact]
    public void AnnotatedMethod_CallsAnnotatedSameAssembly_FiresNothing()
    {
        var source = """
            using MustNotAllocate;

            public class C
            {
                [MustNotAllocate]
                public void Caller() { Callee(); }

                [MustNotAllocate]
                public void Callee() { }
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

- [ ] **Step 2: Run filtered — expect the first to fail, second to pass**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/ --filter "FullyQualifiedName~SameAssemblyBoundary"`
Expected: first FAILS (no CGC001 produced), second PASSES (walker produces nothing anyway).

- [ ] **Step 3: Add boundary detection to ClosureWalker**

Replace the `VisitMethodBody` method in `src/CallgraphClosure.ILCheck.Core/ClosureWalker.cs` with the extended version:

```csharp
    private void VisitMethodBody(
        MethodDefinition method,
        MethodDefinition annotatedCaller,
        ImmutableArray<MethodReference> chain,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (method.Body is null) return;

        foreach (var instruction in method.Body.Instructions)
        {
            // Sink dispatch.
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

            // Call boundary.
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

            if (resolved is not null && HasPropagatingAttribute(resolved))
                continue;

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
```

You also need to add the `using Mono.Cecil.Cil;` directive at the top of `ClosureWalker.cs` (for `Instruction` and `OpCodes`). Add it alongside existing usings.

- [ ] **Step 4: Run filtered — both pass**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/ --filter "FullyQualifiedName~SameAssemblyBoundary"`
Expected: 2 tests pass.

- [ ] **Step 5: Run full suite — 7 tests passing**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/`
Expected: 7 tests pass (5 previous + 2 new).

Note: the Task 8 `AnnotatedMethod_CreatesStruct_DoesNotFireCGC003` test now also emits a CGC001 on the `Point` ctor (which is an unannotated same-assembly call). The test uses `.Where(d => d.Id == DiagnosticIds.SinkHit)` so it still passes. Likewise, the test using `GC.KeepAlive` (if you had to add it in Task 8) will emit a CGC002 but the filter protects the assertion.

- [ ] **Step 6: Commit**

```bash
git add src/CallgraphClosure.ILCheck.Core/ClosureWalker.cs
git add tests/MustNotAllocate.ILCheck.Tests/SameAssemblyBoundaryTests.cs
git commit -m "feat(ilcheck): add CGC001/CGC002 call boundary detection"
```

---

## Task 10: Cross-assembly external call — CGC002 (TDD)

Lock in that a call to an external BCL method produces CGC002 at this stage (before we teach the walker to follow into external assemblies).

**Files:**
- Create: `tests/MustNotAllocate.ILCheck.Tests/CrossAssemblyBoundaryTests.cs`

- [ ] **Step 1: Write the test**

Create `tests/MustNotAllocate.ILCheck.Tests/CrossAssemblyBoundaryTests.cs`:

```csharp
using System.Collections.Immutable;
using System.Linq;
using CallgraphClosure.ILCheck.Core;
using MustNotAllocate.ILCheck;
using Mono.Cecil;
using Xunit;

namespace MustNotAllocate.ILCheck.Tests;

public class CrossAssemblyBoundaryTests
{
    private static ClosureWalker BuildWalker() => new(
        MustNotAllocateIlAnalyzer.AttributeFullName,
        MustNotAllocateIlAnalyzer.Sinks,
        propertyName: "MustNotAllocate");

    [Fact]
    public void AnnotatedMethod_CallsBCLMethod_FiresCGC001Or002()
    {
        // This test asserts the walker reports SOMETHING when calling into the BCL —
        // either CGC002 (external, opaque) or CGC001-style (transitive, resolved).
        // Task 11 tightens this to prefer CGC001 when the BCL is walkable.
        var source = """
            using System;
            using MustNotAllocate;

            public class C
            {
                [MustNotAllocate]
                public void Caller() { Console.WriteLine("hi"); }
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        Assert.NotEmpty(diagnostics);

        var hasBoundaryOrSink = diagnostics.Any(d =>
            d.Id == DiagnosticIds.ExternalBoundary ||
            d.Id == DiagnosticIds.SourceBoundary ||
            d.Id == DiagnosticIds.SinkHit);
        Assert.True(hasBoundaryOrSink);
    }
}
```

- [ ] **Step 2: Run**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/ --filter "FullyQualifiedName~CrossAssemblyBoundary"`
Expected: PASS — at minimum, a CGC002 should fire for `Console.WriteLine` given the current impl.

- [ ] **Step 3: Run full suite — 8 tests passing**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/`
Expected: 8 tests pass.

- [ ] **Step 4: Commit**

```bash
git add tests/MustNotAllocate.ILCheck.Tests/CrossAssemblyBoundaryTests.cs
git commit -m "test(ilcheck): verify cross-assembly calls produce CGC002 baseline"
```

---

## Task 11: Transitive walk through unannotated callees (TDD)

The headline feature. Instead of stopping at an unannotated callee and emitting CGC001/002, **walk into it**. If sinks are found inside the callee or its transitive callees, report CGC003 with the full call chain. Only emit CGC001/002 as a fallback when the callee's body isn't walkable (external + unresolvable).

**Files:**
- Create: `tests/MustNotAllocate.ILCheck.Tests/TransitiveWalkTests.cs`
- Modify: `src/CallgraphClosure.ILCheck.Core/ClosureWalker.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/MustNotAllocate.ILCheck.Tests/TransitiveWalkTests.cs`:

```csharp
using System.Collections.Immutable;
using System.Linq;
using CallgraphClosure.ILCheck.Core;
using MustNotAllocate.ILCheck;
using Mono.Cecil;
using Xunit;

namespace MustNotAllocate.ILCheck.Tests;

public class TransitiveWalkTests
{
    private static ClosureWalker BuildWalker() => new(
        MustNotAllocateIlAnalyzer.AttributeFullName,
        MustNotAllocateIlAnalyzer.Sinks,
        propertyName: "MustNotAllocate");

    [Fact]
    public void AnnotatedCaller_IndirectArrayAllocation_FiresCGC003WithChain()
    {
        // Caller → Helper → new int[]. Helper is unannotated, same assembly.
        var source = """
            using MustNotAllocate;

            public class C
            {
                [MustNotAllocate]
                public void Caller() { Helper(); }

                public void Helper() { var a = new int[10]; }
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        var sinkHits = diagnostics
            .Where(d => d.Id == DiagnosticIds.SinkHit && d.SinkLabel == "array")
            .ToImmutableArray();

        Assert.Single(sinkHits);

        // Chain should be: Caller → Helper (2 entries, innermost last).
        Assert.Equal(2, sinkHits[0].Chain.Length);
        Assert.Equal("Caller", sinkHits[0].Chain[0].Name);
        Assert.Equal("Helper", sinkHits[0].Chain[1].Name);
    }

    [Fact]
    public void AnnotatedCaller_IndirectViaTwoHops_FiresCGC003WithFullChain()
    {
        var source = """
            using MustNotAllocate;

            public class C
            {
                [MustNotAllocate]
                public void A() { B(); }

                public void B() { C_(); }

                public void C_() { var a = new int[10]; }
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        var sinkHits = diagnostics
            .Where(d => d.Id == DiagnosticIds.SinkHit && d.SinkLabel == "array")
            .ToImmutableArray();

        Assert.Single(sinkHits);
        Assert.Equal(3, sinkHits[0].Chain.Length);
        Assert.Equal("A", sinkHits[0].Chain[0].Name);
        Assert.Equal("B", sinkHits[0].Chain[1].Name);
        Assert.Equal("C_", sinkHits[0].Chain[2].Name);
    }
}
```

- [ ] **Step 2: Run — expect failures**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/ --filter "FullyQualifiedName~TransitiveWalk"`
Expected: Both FAIL. The current walker emits CGC001 on `Helper` (and `B`) but doesn't walk into them, so the array sink is missed.

- [ ] **Step 3: Implement transitive walk with cycle guard**

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

    public ClosureWalker(
        string attributeFullName,
        ImmutableArray<IIlSink> sinks,
        string propertyName)
    {
        _attributeFullName = attributeFullName;
        _sinks = sinks;
        _propertyName = propertyName;
    }

    public ImmutableArray<Diagnostic> Analyze(AssemblyDefinition assembly)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (var type in assembly.MainModule.Types)
        {
            foreach (var method in type.Methods)
            {
                if (!HasPropagatingAttribute(method)) continue;

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
            // Sink dispatch.
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

            // Call handling.
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

            // Annotated callee terminates the walk — it made the same promise.
            if (resolved is not null && HasPropagatingAttribute(resolved))
                continue;

            // Walkable body: recurse. Sinks inside become CGC003 attributed to annotatedCaller
            // with an extended chain.
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

            // Unwalkable: emit boundary diagnostic.
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

    private bool HasPropagatingAttribute(MethodDefinition method)
    {
        foreach (var attr in method.CustomAttributes)
        {
            if (attr.AttributeType.FullName == _attributeFullName)
                return true;
        }
        return false;
    }
}
```

Two important semantic points baked into this impl:

1. **Visited set scoped per annotated root.** Re-entering `Analyze` with a fresh `HashSet<string>` for each annotated top-level method means the same unannotated helper can be walked from two different annotated callers independently. This is the correct behavior — each annotated method needs to know its own transitive reach.
2. **Visited set cycles AND shares work.** If Helper calls itself or another visited method inside the current root's walk, the second visit is skipped — prevents infinite loops and avoids duplicate diagnostics.

- [ ] **Step 4: Run the filtered tests**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/ --filter "FullyQualifiedName~TransitiveWalk"`
Expected: Both pass.

- [ ] **Step 5: Run full suite**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/`
Expected: 10 tests pass.

Note: `SameAssemblyBoundaryTests.AnnotatedMethod_CallsUnannotatedSameAssembly_FiresCGC001` still passes because its `Callee` is empty — walk finds no sinks, and the empty body produces no boundary diagnostics either. Actually, the revised walker with `resolved?.Body is not null` treats a method with an EMPTY body (just `ret`) as walkable (body exists, just no instructions apart from `ret`), so the test should still pass because there are no sinks inside the empty Helper — and the walker doesn't emit a CGC001 for a successfully-walked-through empty method. **If that test now fails with "empty diagnostics expected 1," update the test to expect 0 diagnostics**, since the transitive-walk semantics supersede it:

```csharp
// In SameAssemblyBoundaryTests.AnnotatedMethod_CallsUnannotatedSameAssembly_FiresCGC001:
// Change the assertion to:
Assert.Empty(boundaryHits); // walker walked through Callee, found nothing
```

Actually the better fix is to CHANGE THE FIXTURE so Callee is not walkable — e.g., mark it `extern` or give it a body that throws. Since a thrown body still contains `throw` and then... hmm, Cecil will walk through a throwing body and find no sinks. Simpler: update the test to reflect the new transitive semantics. Rename the test to `...WalksThroughEmptyCallee_FiresNothing` and assert empty.

Make that change if needed during this step.

- [ ] **Step 6: Commit**

```bash
git add src/CallgraphClosure.ILCheck.Core/ClosureWalker.cs
git add tests/MustNotAllocate.ILCheck.Tests/TransitiveWalkTests.cs
git add tests/MustNotAllocate.ILCheck.Tests/SameAssemblyBoundaryTests.cs
git commit -m "feat(ilcheck): transitive walk into unannotated callees with cycle guard"
```

---

## Task 12: Annotated callee terminates walk (TDD)

Lock in that calling an annotated helper DOESN'T walk into its body — it trusts the promise. The helper's own violations (if any) are caught when the walker analyzes THAT method directly.

**Files:**
- Create: `tests/MustNotAllocate.ILCheck.Tests/AnnotatedCalleeTerminatesTests.cs`

- [ ] **Step 1: Write the tests**

Create `tests/MustNotAllocate.ILCheck.Tests/AnnotatedCalleeTerminatesTests.cs`:

```csharp
using System.Collections.Immutable;
using System.Linq;
using CallgraphClosure.ILCheck.Core;
using MustNotAllocate.ILCheck;
using Mono.Cecil;
using Xunit;

namespace MustNotAllocate.ILCheck.Tests;

public class AnnotatedCalleeTerminatesTests
{
    private static ClosureWalker BuildWalker() => new(
        MustNotAllocateIlAnalyzer.AttributeFullName,
        MustNotAllocateIlAnalyzer.Sinks,
        propertyName: "MustNotAllocate");

    [Fact]
    public void AnnotatedCallerTrustsAnnotatedHelper_CallerWalkStopsAtHelper()
    {
        // Helper is annotated but allocates. Caller trusts Helper — no diagnostic attributed
        // to Caller. Helper's own walk finds the allocation and attributes it to Helper.
        var source = """
            using MustNotAllocate;

            public class C
            {
                [MustNotAllocate]
                public void Caller() { Helper(); }

                [MustNotAllocate]
                public void Helper() { var a = new int[10]; }
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        var sinkHits = diagnostics
            .Where(d => d.Id == DiagnosticIds.SinkHit)
            .ToImmutableArray();

        // Only one diagnostic: attributed to Helper, not Caller.
        Assert.Single(sinkHits);
        Assert.Equal("Helper", sinkHits[0].AnnotatedCaller.Name);
    }
}
```

- [ ] **Step 2: Run**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/ --filter "FullyQualifiedName~AnnotatedCalleeTerminates"`
Expected: PASS immediately (the Task 11 impl already has the `continue` for annotated callees).

- [ ] **Step 3: Run full suite**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/`
Expected: 11 tests pass.

- [ ] **Step 4: Commit**

```bash
git add tests/MustNotAllocate.ILCheck.Tests/AnnotatedCalleeTerminatesTests.cs
git commit -m "test(ilcheck): verify annotated callees terminate transitive walk"
```

---

## Task 13: Cycle guard (TDD)

Explicit regression test: mutual recursion doesn't hang the walker.

**Files:**
- Create: `tests/MustNotAllocate.ILCheck.Tests/CycleGuardTests.cs`

- [ ] **Step 1: Write the test**

Create `tests/MustNotAllocate.ILCheck.Tests/CycleGuardTests.cs`:

```csharp
using System.Collections.Immutable;
using System.Linq;
using CallgraphClosure.ILCheck.Core;
using MustNotAllocate.ILCheck;
using Mono.Cecil;
using Xunit;

namespace MustNotAllocate.ILCheck.Tests;

public class CycleGuardTests
{
    private static ClosureWalker BuildWalker() => new(
        MustNotAllocateIlAnalyzer.AttributeFullName,
        MustNotAllocateIlAnalyzer.Sinks,
        propertyName: "MustNotAllocate");

    [Fact]
    public void MutualRecursion_TerminatesWithoutHanging_FiresOneSinkHit()
    {
        var source = """
            using MustNotAllocate;

            public class C
            {
                [MustNotAllocate]
                public void A() { B(); var a = new int[10]; }

                public void B() { A(); }
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        var sinkHits = diagnostics
            .Where(d => d.Id == DiagnosticIds.SinkHit)
            .ToImmutableArray();

        Assert.Single(sinkHits);
        Assert.Equal("array", sinkHits[0].SinkLabel);
    }
}
```

- [ ] **Step 2: Run — should pass (cycle guard implemented in Task 11)**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/ --filter "FullyQualifiedName~CycleGuard"`
Expected: PASS. If the test hangs, kill it with `Ctrl-C` and verify the `visited.Add(method.FullName)` check is present in `VisitMethodBody`.

- [ ] **Step 3: Run full suite**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/`
Expected: 12 tests pass.

- [ ] **Step 4: Commit**

```bash
git add tests/MustNotAllocate.ILCheck.Tests/CycleGuardTests.cs
git commit -m "test(ilcheck): verify mutual recursion terminates via cycle guard"
```

---

## Task 14: Cross-assembly transitive walk outcome verification (TDD)

Now that transitive walking is implemented, re-verify the cross-assembly case from Task 10. The outcome depends on whether `Console.WriteLine` resolves to a walkable body in the test host's runtime directory. Either outcome is valid per the spec's "Open uncertainty":

- If walkable: we'll likely find a CGC003 sink several hops into `Console.WriteLine`'s implementation.
- If unwalkable (ref-assembly-only stubs): we'll get a CGC002 on the outer call.

This task tightens the assertion from "something fired" to "either a CGC002 on `WriteLine` OR a CGC003 somewhere under it" — and prints the full diagnostic list for the writeup if we got a CGC003.

**Files:**
- Modify: `tests/MustNotAllocate.ILCheck.Tests/CrossAssemblyBoundaryTests.cs`

- [ ] **Step 1: Replace the existing test with a more specific assertion**

Overwrite `tests/MustNotAllocate.ILCheck.Tests/CrossAssemblyBoundaryTests.cs`:

```csharp
using System.Collections.Immutable;
using System.Linq;
using CallgraphClosure.ILCheck.Core;
using MustNotAllocate.ILCheck;
using Mono.Cecil;
using Xunit;
using Xunit.Abstractions;

namespace MustNotAllocate.ILCheck.Tests;

public class CrossAssemblyBoundaryTests
{
    private readonly ITestOutputHelper _output;

    public CrossAssemblyBoundaryTests(ITestOutputHelper output) => _output = output;

    private static ClosureWalker BuildWalker() => new(
        MustNotAllocateIlAnalyzer.AttributeFullName,
        MustNotAllocateIlAnalyzer.Sinks,
        propertyName: "MustNotAllocate");

    [Fact]
    public void AnnotatedMethod_CallsConsoleWriteLine_ProducesCGC002OrUpgradedCGC003()
    {
        var source = """
            using System;
            using MustNotAllocate;

            public class C
            {
                [MustNotAllocate]
                public void Caller() { Console.WriteLine("hi"); }
            }
            """;

        var dllPath = CompileFixture.Emit(source);
        using var assembly = AssemblyDefinition.ReadAssembly(
            dllPath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(dllPath) });

        var diagnostics = BuildWalker().Analyze(assembly);

        // Dump everything for the writeup / for investigation on CI.
        foreach (var d in diagnostics)
        {
            var chainStr = string.Join(" -> ", d.Chain.Select(m => m.Name));
            _output.WriteLine($"{d.Id} ({d.SinkLabel ?? "-"}): {chainStr}");
        }

        // Outcome A: walk reached a sink inside the BCL → CGC003 upgraded from CGC002.
        // Outcome B: walk hit a ref-only body or an unresolvable call → CGC002.
        var hasCGC003Upgrade = diagnostics.Any(d =>
            d.Id == DiagnosticIds.SinkHit && d.Chain.Length > 1);
        var hasCGC002 = diagnostics.Any(d => d.Id == DiagnosticIds.ExternalBoundary);

        Assert.True(
            hasCGC003Upgrade || hasCGC002,
            "Expected either a transitively-found sink (CGC003) or an unresolved external (CGC002).");
    }
}
```

- [ ] **Step 2: Run, capture output**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/ --filter "FullyQualifiedName~CrossAssemblyBoundary" --logger "console;verbosity=detailed"`
Expected: PASS. The test output will list the diagnostics produced — this is informative, not asserted beyond the "at least one" constraint. **Include this output in your report** so we know whether the BCL was walkable or not.

- [ ] **Step 3: Run full suite**

Run: `dotnet test tests/MustNotAllocate.ILCheck.Tests/`
Expected: 12 tests pass (unchanged count; we replaced the existing test).

- [ ] **Step 4: Commit**

```bash
git add tests/MustNotAllocate.ILCheck.Tests/CrossAssemblyBoundaryTests.cs
git commit -m "test(ilcheck): tighten cross-assembly test to CGC002-or-upgraded-CGC003"
```

---

## Task 15: CLI entry point

Thin console wrapper that loads an assembly, runs the walker, and prints human-readable output.

**Files:**
- Create: `src/CallgraphClosure.ILCheck.Cli/CallgraphClosure.ILCheck.Cli.csproj`
- Create: `src/CallgraphClosure.ILCheck.Cli/Program.cs`
- Create: `src/CallgraphClosure.ILCheck.Cli/DiagnosticFormatter.cs`

- [ ] **Step 1: Create the project**

Run:
```bash
dotnet new console -o src/CallgraphClosure.ILCheck.Cli -f net10.0
rm src/CallgraphClosure.ILCheck.Cli/Program.cs
dotnet sln add src/CallgraphClosure.ILCheck.Cli/CallgraphClosure.ILCheck.Cli.csproj
```

- [ ] **Step 2: Configure the .csproj**

Overwrite `src/CallgraphClosure.ILCheck.Cli/CallgraphClosure.ILCheck.Cli.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>cgc-ilcheck</AssemblyName>
    <RootNamespace>CallgraphClosure.ILCheck.Cli</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CallgraphClosure.ILCheck.Core\CallgraphClosure.ILCheck.Core.csproj" />
    <ProjectReference Include="..\MustNotAllocate.ILCheck\MustNotAllocate.ILCheck.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write DiagnosticFormatter**

Create `src/CallgraphClosure.ILCheck.Cli/DiagnosticFormatter.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CallgraphClosure.ILCheck.Core;

namespace CallgraphClosure.ILCheck.Cli;

public static class DiagnosticFormatter
{
    public static string Format(
        string inputPath,
        IReadOnlyList<Diagnostic> diagnostics)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== CallgraphClosure IL Check ===");
        sb.AppendLine($"Input: {inputPath}");

        var byCaller = diagnostics
            .GroupBy(d => d.AnnotatedCaller.FullName)
            .ToList();

        sb.AppendLine($"Annotated methods with diagnostics: {byCaller.Count}");
        sb.AppendLine();

        foreach (var group in byCaller)
        {
            sb.AppendLine($"Method {group.Key}:");
            foreach (var d in group)
            {
                var kind = d.Id switch
                {
                    DiagnosticIds.SinkHit when d.Chain.Length > 1
                        => $"[CGC003] {d.SinkLabel} allocation (upgraded from CGC002)",
                    DiagnosticIds.SinkHit
                        => $"[CGC003] {d.SinkLabel} allocation",
                    DiagnosticIds.SourceBoundary
                        => "[CGC001] unannotated source call (unresolved)",
                    DiagnosticIds.ExternalBoundary
                        => "[CGC002] unannotated external call (unresolved)",
                    _ => $"[{d.Id}]",
                };
                sb.AppendLine($"  {kind}");
                foreach (var frame in d.Chain)
                    sb.AppendLine($"    -> {frame.FullName}");
                if (d.UnresolvedTarget is not null)
                    sb.AppendLine($"    (unresolved target: {d.UnresolvedTarget.FullName})");
            }
            sb.AppendLine();
        }

        var counts = diagnostics.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.Count());
        sb.AppendLine(
            $"Summary: CGC001={counts.GetValueOrDefault(DiagnosticIds.SourceBoundary, 0)}, " +
            $"CGC002={counts.GetValueOrDefault(DiagnosticIds.ExternalBoundary, 0)}, " +
            $"CGC003={counts.GetValueOrDefault(DiagnosticIds.SinkHit, 0)}");

        return sb.ToString();
    }
}
```

- [ ] **Step 4: Write Program.cs**

Create `src/CallgraphClosure.ILCheck.Cli/Program.cs`:

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
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: cgc-ilcheck <path-to-assembly>");
            return 2;
        }

        var path = args[0];
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Error: file not found: {path}");
            return 2;
        }

        using var assembly = AssemblyDefinition.ReadAssembly(
            path,
            new ReaderParameters
            {
                AssemblyResolver = AssemblyResolver.ForAssemblyPath(path),
            });

        var walker = new ClosureWalker(
            MustNotAllocateIlAnalyzer.AttributeFullName,
            MustNotAllocateIlAnalyzer.Sinks,
            propertyName: "MustNotAllocate");

        var diagnostics = walker.Analyze(assembly);

        Console.Write(DiagnosticFormatter.Format(path, diagnostics));

        return diagnostics.Length == 0 ? 0 : 1;
    }
}
```

- [ ] **Step 5: Build**

Run: `dotnet build src/CallgraphClosure.ILCheck.Cli/`
Expected: 0/0.

- [ ] **Step 6: Smoke-run against M1 sample**

Run:
```bash
dotnet build src/MustNotAllocate.Sample/
dotnet run --project src/CallgraphClosure.ILCheck.Cli/ -- \
    src/MustNotAllocate.Sample/bin/Debug/net10.0/MustNotAllocate.Sample.dll
```

Expected: the tool prints something useful. At minimum one CGC003 for the `new int[16]` direct allocation inside `Tick`. Ideally also an upgraded CGC003 or CGC002 for the `Console.WriteLine` call.

**Include the full output in your report** — this is the writeup's headline moment.

- [ ] **Step 7: Commit**

```bash
git add src/CallgraphClosure.ILCheck.Cli/
git commit -m "feat(ilcheck): add CLI with human-readable diagnostic output"
```

---

## Task 16: End-to-end test against the compiled M1 sample

Assert that running the analyzer against the real compiled sample produces at least the expected direct sink.

**Files:**
- Create: `tests/MustNotAllocate.ILCheck.Tests/EndToEndSampleTests.cs`

- [ ] **Step 1: Write the test**

Create `tests/MustNotAllocate.ILCheck.Tests/EndToEndSampleTests.cs`:

```csharp
using System.IO;
using System.Linq;
using CallgraphClosure.ILCheck.Core;
using MustNotAllocate.ILCheck;
using Mono.Cecil;
using Xunit;
using Xunit.Abstractions;

namespace MustNotAllocate.ILCheck.Tests;

public class EndToEndSampleTests
{
    private readonly ITestOutputHelper _output;

    public EndToEndSampleTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ILCheck_OnCompiledSample_FindsTheArrayAllocation()
    {
        // This test relies on `dotnet build` having produced the sample DLL.
        // Normally happens automatically because the test project and sample are
        // both in the solution and the test harness builds the whole graph.
        var repoRoot = FindRepoRoot();
        var samplePath = Path.Combine(
            repoRoot,
            "src", "MustNotAllocate.Sample", "bin", "Debug", "net10.0",
            "MustNotAllocate.Sample.dll");

        Assert.True(
            File.Exists(samplePath),
            $"Compiled sample not found at {samplePath} — build the solution first.");

        using var assembly = AssemblyDefinition.ReadAssembly(
            samplePath,
            new ReaderParameters { AssemblyResolver = AssemblyResolver.ForAssemblyPath(samplePath) });

        var walker = new ClosureWalker(
            MustNotAllocateIlAnalyzer.AttributeFullName,
            MustNotAllocateIlAnalyzer.Sinks,
            propertyName: "MustNotAllocate");

        var diagnostics = walker.Analyze(assembly);

        // Log everything for the writeup.
        foreach (var d in diagnostics)
        {
            var chainStr = string.Join(" -> ", d.Chain.Select(m => m.Name));
            _output.WriteLine($"{d.Id} ({d.SinkLabel ?? "-"}): {chainStr}");
        }

        // Must find at least the direct array allocation inside Tick.
        var directArray = diagnostics.FirstOrDefault(d =>
            d.Id == DiagnosticIds.SinkHit &&
            d.SinkLabel == "array" &&
            d.AnnotatedCaller.Name == "Tick");

        Assert.NotNull(directArray);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CallgraphClosure.sln")))
            dir = dir.Parent;
        if (dir is null)
            throw new FileNotFoundException("Could not locate repo root (CallgraphClosure.sln)");
        return dir.FullName;
    }
}
```

- [ ] **Step 2: Build the sample, then run the end-to-end test**

Run:
```bash
dotnet build CallgraphClosure.sln
dotnet test tests/MustNotAllocate.ILCheck.Tests/ --filter "FullyQualifiedName~EndToEndSample"
```

Expected: PASS. The test output should list all diagnostics the walker found — include this in your report, since it's the concrete evidence the two-pass architecture works.

- [ ] **Step 3: Run full suite**

Run: `dotnet test`
Expected: all tests pass — M1 (18) + M2 (13 = smoke + 4 direct + 2 same-assembly + 1 cross-assembly + 2 transitive + 1 annotated-terminates + 1 cycle + 1 end-to-end) = 31 tests.

- [ ] **Step 4: Tag the milestone**

Run:
```bash
git tag -a m2-complete -m "Milestone 2: Cecil IL post-pass with transitive walk and CLI"
```

Expected: `git tag --list` shows `m1-complete` and `m2-complete`.

- [ ] **Step 5: Commit the test**

```bash
git add tests/MustNotAllocate.ILCheck.Tests/EndToEndSampleTests.cs
git commit -m "test(ilcheck): end-to-end check against compiled M1 sample"
```
