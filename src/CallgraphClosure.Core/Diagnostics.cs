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
        messageFormat: "Method '{0}' is annotated [{1}] but contains a {2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
