using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Text;

namespace Luces.Mediator.SourceGen;

internal static class SymbolAnalysis
{
    public static bool HasGenericContainingType(INamedTypeSymbol type)
    {
        for (var containing = type.ContainingType;
             containing is not null;
             containing = containing.ContainingType)
        {
            if (containing.IsGenericType) return true;
        }

        return false;
    }

    public static string? EffectiveVisibility(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array) return EffectiveVisibility(array.ElementType);
        if (type is ITypeParameterSymbol or IDynamicTypeSymbol) return "public";
        if (type is not INamedTypeSymbol named) return null;
        if (named.IsFileLocal || named.IsRefLikeType) return null;

        var accessibility = named.DeclaredAccessibility;
        for (var containing = named.ContainingType;
             containing is not null;
             containing = containing.ContainingType)
        {
            if (containing.IsFileLocal) return null;
            accessibility = MinAccessibility(accessibility, containing.DeclaredAccessibility);
        }

        var result = ToVisibility(accessibility);
        if (result is null) return null;

        foreach (var argument in named.TypeArguments)
        {
            result = MinVisibility(result, EffectiveVisibility(argument));
            if (result is null) return null;
        }

        for (var containing = named.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            foreach (var argument in containing.TypeArguments)
            {
                result = MinVisibility(result, EffectiveVisibility(argument));
                if (result is null) return null;
            }
        }

        return result;
    }

    public static string? EffectiveConstraintVisibility(INamedTypeSymbol type)
    {
        var result = "public";
        foreach (var parameter in type.TypeParameters)
        {
            foreach (var constraint in parameter.ConstraintTypes)
            {
                result = MinVisibility(result, EffectiveVisibility(constraint));
                if (result is null) return null;
            }
        }

        return result;
    }

    private static Accessibility MinAccessibility(Accessibility left, Accessibility right)
    {
        return Rank(left) <= Rank(right) ? left : right;
    }

    private static int Rank(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => 5,
        Accessibility.ProtectedOrInternal => 4,
        Accessibility.Internal => 3,
        Accessibility.Protected => 2,
        Accessibility.ProtectedAndInternal => 1,
        _ => 0,
    };

    private static string? ToVisibility(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal or Accessibility.ProtectedOrInternal => "internal",
        _ => null,
    };

    private static string? MinVisibility(string? left, string? right)
    {
        return left is null || right is null ? null : left == "internal" || right == "internal" ? "internal" : "public";
    }
}
