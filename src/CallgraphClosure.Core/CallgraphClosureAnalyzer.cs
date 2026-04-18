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

        var amortizedFileMethods = LoadAmortizedFileMethods(
            c.Options.AdditionalFiles, c.CancellationToken);

        c.RegisterOperationBlockAction(b =>
            AnalyzeBlock(b, attrSym, amortizedSym, amortizedFileMethods, c.Compilation));
    }

    private ImmutableHashSet<string> LoadAmortizedFileMethods(
        ImmutableArray<AdditionalText> additionalFiles,
        System.Threading.CancellationToken cancellationToken)
    {
        foreach (var file in additionalFiles)
        {
            if (System.IO.Path.GetFileName(file.Path) != _config.AmortizedFileName)
                continue;

            var text = file.GetText(cancellationToken);
            if (text is null) continue;

            try
            {
                return ParseAmortizedJson(text.ToString());
            }
            catch (System.FormatException)
            {
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

        if (arr.ValueKind != System.Text.Json.JsonValueKind.Array)
            return ImmutableHashSet<string>.Empty;

        var builder = ImmutableHashSet.CreateBuilder<string>();
        foreach (var element in arr.EnumerateArray())
        {
            if (element.ValueKind != System.Text.Json.JsonValueKind.String) continue;
            var name = element.GetString();
            if (!string.IsNullOrWhiteSpace(name))
                builder.Add(name!);
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
        // Produce a Roslyn-display FQN: "ContainingType.MethodName(ParamType1, ParamType2)"
        var paramList = string.Join(", ",
            method.Parameters.Select(p => p.Type.ToDisplayString()));
        return $"{method.ContainingType.ToDisplayString()}.{method.Name}({paramList})";
    }

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attrSym) =>
        symbol.GetAttributes().Any(a =>
            SymbolEqualityComparer.Default.Equals(a.AttributeClass, attrSym));
}
