using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Text;

namespace Luces.Mediator.SourceGen;

internal static class SymbolExtensions
{
    private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static string ToFqn(this ITypeSymbol symbol)
    {
        return symbol.ToDisplayString(FullyQualified);
    }

    /// <summary>
    /// Escapes a symbol name for direct inclusion in source text. Symbol names never carry the '@'
    /// verbatim prefix even when the declaration used one (e.g. the type parameter of
    /// <c>Query&lt;@class&gt;</c> is named <c>class</c>), so reserved keywords must be escaped again
    /// when the name is written outside of Roslyn's type formatting.
    /// </summary>
    public static string EscapeIdentifier(string name)
    {
        return SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;
    }

    public static string ToMetadataName(this INamedTypeSymbol symbol)
    {
        var types = new Stack<string>();
        for (var current = symbol; current is not null; current = current.ContainingType)
        {
            types.Push(current.MetadataName);
        }

        var typeName = string.Join("+", types);
        return symbol.ContainingNamespace.IsGlobalNamespace ? typeName : symbol.ContainingNamespace.ToDisplayString() + "." + typeName;
    }

    public static string? ToFqnNoGlobal(this INamespaceSymbol symbol)
    {
        if (symbol.IsGlobalNamespace) return null;

        var parts = new Stack<string>();
        for (var current = symbol;
             current is not null && !current.IsGlobalNamespace;
             current = current.ContainingNamespace)
        {
            parts.Push(current.Name);
        }

        return string.Join(".", parts);
    }
}
