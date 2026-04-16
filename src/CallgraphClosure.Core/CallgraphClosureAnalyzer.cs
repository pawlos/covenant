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
