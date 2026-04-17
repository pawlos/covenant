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

        var isExternal = !SymbolEqualityComparer.Default.Equals(
            original.ContainingAssembly, compilation.Assembly);

        var descriptor = isExternal
            ? Diagnostics.ExternalBoundary
            : Diagnostics.SourceBoundary;

        // For constructors, use the containing type name (e.g. "Foo") rather than
        // the method name (".ctor") so diagnostics are more readable.
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
