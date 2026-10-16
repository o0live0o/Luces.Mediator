using Microsoft.CodeAnalysis;

namespace Luces.Mediator.SourceGen;

internal static class SenderGeneratorDiagnostics
{
    public static readonly DiagnosticDescriptor MultipleResponseTypes = new(id: "MED001",
        title: "Request implements multiple response types",
        messageFormat: "Type '{0}' implements {1}<> with multiple distinct response types ({2}); sender convenience overload skipped \u2014 call sites must keep explicit generic arguments",
        category: "Mediator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SenderNotFound = new(
        id: "MED002",
        title: "Sender interface not found",
        messageFormat: "SenderExtensionsGenerator is enabled but no interface '{0}' was found in the compilation; ensure the assembly defining it is referenced, or set <LucesMediatorExtensionsSenderType> to the correct simple name",
        category: "Mediator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AmbiguousInterface = new(
        id: "MED003",
        title: "Mediator interface name is ambiguous",
        messageFormat: "Interface setting '{0}' matches multiple symbols ({1}); use its fully qualified metadata name",
        category: "Mediator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OutputNamespaceRequired = new(
        id: "MED004",
        title: "Generated namespace is required",
        messageFormat: "Sender interface '{0}' is in the global namespace; set <LucesMediatorExtensionsNamespace> to a valid namespace",
        category: "Mediator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidOption = new(
        id: "MED005",
        title: "Invalid sender generator option",
        messageFormat: "MSBuild property '{0}' has invalid C# value '{1}'; sender convenience overload generation was skipped",
        category: "Mediator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ExtensionClassMethodNameConflict = new(
        id: "MED006",
        title: "Extension class name collides with a generated method name",
        messageFormat: "MSBuild property '{0}' value '{1}' equals '{2}' value '{3}'; the generated extension class cannot contain a member with the same name as the class, so sender convenience overload generation was skipped",
        category: "Mediator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GeneratedMethodSignatureConflict = new(
        id: "MED007",
        title: "Generated extension methods share a signature",
        messageFormat: "Generated method '{0}' for request '{1}' would duplicate another generated overload with the same parameter list; the request implements both the request and stream-request interfaces while the configured send and stream method names are identical, so the duplicate overload was skipped \u2014 C# cannot overload on return type alone",
        category: "Mediator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}