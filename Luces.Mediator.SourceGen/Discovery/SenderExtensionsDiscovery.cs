using Luces.Mediator.SourceGen.Configuration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Luces.Mediator.SourceGen.Discovery;

internal static class SenderExtensionsDiscovery
{
    public static bool IsCandidate(SyntaxNode node)
    {
        return node is TypeDeclarationSyntax declaration
            && declaration is ClassDeclarationSyntax
            or RecordDeclarationSyntax
            or StructDeclarationSyntax
            && declaration.BaseList is { Types.Count: > 0 }
            && !HasStaticOrAbstractModifier(declaration);
    }

    public static SenderGenerationModel? Discover(SourceProductionContext context, Compilation compilation, ImmutableArray<INamedTypeSymbol?> symbols, SenderGeneratorOptions options)
    {
        var sender = FindInterface(context, compilation, options.SenderInterfaceName, expectedArity: 0, preferredContainingNamespace: null, out var senderAmbiguous);

        if (sender is null)
        {
            if (!senderAmbiguous)
            {
                context.ReportDiagnostic(Diagnostic.Create(SenderGeneratorDiagnostics.SenderNotFound, Location.None, options.SenderInterfaceName));
            }

            return null;
        }

        var request = FindInterface(context, compilation, options.RequestInterfaceName, expectedArity: 1, preferredContainingNamespace: sender.ContainingNamespace, out _);

        var streamRequest = FindInterface(context, compilation, options.StreamRequestInterfaceName, expectedArity: 1, preferredContainingNamespace: sender.ContainingNamespace, out _);

        if (request is null && streamRequest is null) return null;

        var methods = DiscoverMethods(context, symbols, request, streamRequest, options);

        if (methods.IsDefaultOrEmpty) return null;

        var outputNamespace = options.OutputNamespaceOverride ?? sender.ContainingNamespace.ToFqnNoGlobal();

        if (outputNamespace is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(SenderGeneratorDiagnostics.OutputNamespaceRequired, Location.None, sender.ToFqn()));

            return null;
        }

        return new SenderGenerationModel(sender, outputNamespace, methods);
    }

    private static void AddMethod(SourceProductionContext context, INamedTypeSymbol request, List<ITypeSymbol?> responses, bool isStream, string interfaceName, List<SenderExtensionMethod> methods)
    {
        if (responses.Count == 0) return;

        if (responses.Count > 1)
        {
            var distinct = responses.Distinct<ITypeSymbol>(SymbolEqualityComparer.Default).ToList();

            if (distinct.Count > 1)
            {
                context.ReportDiagnostic(Diagnostic.Create(SenderGeneratorDiagnostics.MultipleResponseTypes, request.Locations.FirstOrDefault() ?? Location.None, request.ToFqn(), interfaceName, string.Join(", ", distinct.Select(type => type.ToFqn()))));
                return;
            }

            responses = distinct;
        }

        methods.Add(new SenderExtensionMethod(isStream, request, responses[0]));
    }

    private static ImmutableArray<SenderExtensionMethod> DiscoverMethods(SourceProductionContext context, ImmutableArray<INamedTypeSymbol> symbols, INamedTypeSymbol request, INamedTypeSymbol streamRequest, SenderGeneratorOptions options)
    {
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var methods = new List<SenderExtensionMethod>();

        foreach (var symbol in symbols)
        {
            if (symbol is null) continue;
            if (symbol.IsAbstract || symbol.IsStatic || symbol.IsImplicitlyDeclared) continue;
            if (symbol.IsUnboundGenericType) continue;
            if (!seen.Add(symbol)) continue;
            if (SymbolAnalysis.EffectiveVisibility(symbol) is null) continue;
            if (SymbolAnalysis.HasGenericContainingType(symbol)) continue;

            var requestResponses = new List<ITypeSymbol?>();
            var streamResponses = new List<ITypeSymbol?>();

            foreach (var @interface in symbol.AllInterfaces)
            {
                if (@interface.TypeArguments.Length != 1) continue;

                var definition = @interface.OriginalDefinition;
                if (request is not null && SymbolEqualityComparer.Default.Equals(definition, request))
                {
                    requestResponses.Add(@interface.TypeArguments[0]);
                }
                else if (streamRequest is not null && SymbolEqualityComparer.Default.Equals(definition, streamRequest))
                {
                    streamResponses.Add(@interface.TypeArguments[0]);
                }
            }

            AddMethod(context, symbol, requestResponses, isStream: false, options.RequestInterfaceName, methods);

            AddMethod(context, symbol, streamResponses, isStream: true, options.StreamRequestInterfaceName, methods);
        }

        methods.Sort((left, right) =>
        {
            int result = string.CompareOrdinal(left.Request.ToFqn(), right.Request.ToFqn());
            if (result != 0) return result;

            result = left.IsStream.CompareTo(right.IsStream);
            if (result != 0)
                return result;

            return string.CompareOrdinal(left.Response?.ToFqn(), right.Response?.ToFqn());
        });

        // A request implementing both the request and the stream-request interfaces produces a send and
        // a stream overload. When the two configured method names are equal those overloads end up with
        // the same parameter list and differ only by return type, which C# cannot overload (CS0111).
        // Keep the first overload in sort order (the send one) and diagnose the duplicate.
        var signatures = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < methods.Count; index++)
        {
            var method = methods[index];
            var methodName = method.IsStream ? options.StreamMethodName : options.SendMethodName;
            var signature = methodName + "\u0000" + method.Request.ToFqn();

            if (signatures.Add(signature)) continue;

            context.ReportDiagnostic(Diagnostic.Create(SenderGeneratorDiagnostics.GeneratedMethodSignatureConflict, Location.None, methodName, method.Request.ToFqn()));
            methods.RemoveAt(index);
            index--;
        }

        return methods.ToImmutableArray();
    }

    private static INamedTypeSymbol? FindInterface(SourceProductionContext context, Compilation compilation, string configuredName, int expectedArity, INamespaceSymbol? preferredContainingNamespace, out bool ambiguous)
    {
        ambiguous = false;

        var normalizedName = configuredName.StartsWith("global::", StringComparison.Ordinal) ? configuredName.Substring("global::".Length) : configuredName;

        var isQualified = normalizedName.IndexOf('.') >= 0 || normalizedName.IndexOf('+') >= 0;
        var expectedName = AddArity(normalizedName, expectedArity);

        IEnumerable<INamedTypeSymbol> candidates = isQualified ? compilation.GetTypesByMetadataName(expectedName) : compilation.GetSymbolsWithName(normalizedName, SymbolFilter.Type, context.CancellationToken).OfType<INamedTypeSymbol>();

        var matches = candidates.Where(type => type.TypeKind == TypeKind.Interface && type.Arity == expectedArity).ToList();

        if (!isQualified && preferredContainingNamespace is not null)
        {
            var preferred = matches.Where(type => SymbolEqualityComparer.Default.Equals(type.ContainingNamespace, preferredContainingNamespace)).ToList();

            if (preferred.Count == 1)
                return preferred[0];

            if (preferred.Count > 1)
            {
                ambiguous = true;
                ReportAmbiguity(context, configuredName, preferred);
                return null;
            }
        }

        if (matches.Count == 1)
            return matches[0];

        if (matches.Count > 1)
        {
            ambiguous = true;
            ReportAmbiguity(context, configuredName, matches);
            return null;
        }

        return null;
    }

    private static void ReportAmbiguity(SourceProductionContext context, string configuredName, IReadOnlyList<INamedTypeSymbol> matches)
    {
        context.ReportDiagnostic(Diagnostic.Create(SenderGeneratorDiagnostics.AmbiguousInterface, Location.None, configuredName, string.Join(", ", matches.Select(type => type.ToFqn()))));
    }

    private static bool HasStaticOrAbstractModifier(TypeDeclarationSyntax declaration)
    {
        foreach (var modifier in declaration.Modifiers)
        {
            if (modifier.IsKind(SyntaxKind.StaticKeyword) || modifier.IsKind(SyntaxKind.AbstractKeyword))
            {
                return true;
            }
        }

        return false;
    }

    private static string AddArity(string name, int arity)
    {
        return arity == 0 || name.IndexOf('`') >= 0 ? name : name + "`" + arity;
    }
}

internal readonly record struct SenderGenerationModel(INamedTypeSymbol Sender, string OutputNamespace, ImmutableArray<SenderExtensionMethod> Methods);

internal readonly record struct SenderExtensionMethod(bool IsStream, INamedTypeSymbol Request, ITypeSymbol Response);