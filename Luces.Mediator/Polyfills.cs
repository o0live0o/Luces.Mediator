// Polyfills for pre-.NET6 targets. Guarded so modern TFMs keep using the BCL implementations.
// Block-scoped namespaces throughout: a file-scoped namespace cannot follow type declarations.
#if !NET6_0_OR_GREATER

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    internal sealed class CallerArgumentExpressionAttribute : Attribute
    {
        public CallerArgumentExpressionAttribute(string parameterName) { }
    }
}

namespace System
{
    internal static class ThrowHelper
    {
        public static void ThrowIfNull(object? argument, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument is null)
            {
                throw new ArgumentNullException(paramName);
            }
        }
    }
}

#endif

namespace Luces.Mediator
{
    /// <summary>Single call site for null checks: BCL on .NET 6+, local helper on older TFMs.</summary>
    internal static class Guard
    {
        public static void NotNull(object? argument, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(argument, paramName);
#else
            System.ThrowHelper.ThrowIfNull(argument, paramName);
#endif
        }
    }
}
