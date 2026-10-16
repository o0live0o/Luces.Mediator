using Luces.Mediator.SourceGen.Configuration;
using Luces.Mediator.SourceGen.Discovery;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Luces.Mediator.SourceGen;

internal static class SenderExtensionsEmitter
{
    private const string TemplateResource = "Luces.Mediator.SourceGen.Templates.GeneratedSenderExtensions.cs";
    private const string NamespaceToken = "__OUTPUT_NAMESPACE__";
    private const string ClassNameToken = "__CLASS_NAME__";
    private const string MethodsToken = "// __METHODS__";

    // The sender interface members that generated extensions delegate to. These are fixed so that
    // renaming the generated extension method (e.g. to Dispatch) never changes the underlying call.
    private const string SendTargetMethod = "Send";
    private const string StreamTargetMethod = "CreateStream";

    public static string Emit(SenderGenerationModel model, SenderGeneratorOptions options)
    {
        var writer = new CodeWriter();
        int emittedCount = 0;

        foreach (var method in model.Methods)
        {
            var visibility = GetVisibility(model.Sender, method);
            if (visibility is null) continue;

            if (emittedCount > 0) writer.WriteLine();

            WriteMethod(writer, method, model.Sender.ToFqn(), visibility, options);
            emittedCount++;
        }

        var source = ReplaceRequiredToken(LoadTemplate(), NamespaceToken, model.OutputNamespace);

        source = ReplaceRequiredToken(source, ClassNameToken, options.ClassName);

        return ReplaceIndentedToken(source, MethodsToken, writer.ToStringWithoutFinalNewLine());
    }

    private static string? GetVisibility(INamedTypeSymbol sender, SenderExtensionMethod method)
    {
        var senderVisibility = SymbolAnalysis.EffectiveVisibility(sender);
        var requestVisibility = SymbolAnalysis.EffectiveVisibility(method.Request);
        var responseVisibility = SymbolAnalysis.EffectiveVisibility(method.Response);
        var constraintVisibility = SymbolAnalysis.EffectiveConstraintVisibility(method.Request);

        if (senderVisibility is null || requestVisibility is null || responseVisibility is null || constraintVisibility is null)
            return null;

        return senderVisibility == "internal" || requestVisibility == "internal" || responseVisibility == "internal" || constraintVisibility == "internal" ? "internal" : "public";
    }

    private static void WriteMethod(CodeWriter writer, SenderExtensionMethod method, string senderTypeFqn, string visibility, SenderGeneratorOptions options)
    {
        var requestRef = method.Request.ToFqn();
        var responseRef = method.Response.ToFqn();

        // The public name of the generated extension method comes from configuration; the member it
        // forwards to is always the sender's fixed Send/CreateStream method.
        var methodName = method.IsStream ? options.StreamMethodName : options.SendMethodName;
        var targetMethodName = method.IsStream ? StreamTargetMethod : SendTargetMethod;

        string returnType;
        if (method.IsStream)
        {
            returnType = $"global::System.Collections.Generic.IAsyncEnumerable<{responseRef}>";
        }
        else
        {
            returnType = $"global::System.Threading.Tasks.ValueTask<{responseRef}>";
        }

        string methodGenerics;
        if (method.Request.IsGenericType)
        {
            methodGenerics = $"<{string.Join(", ", method.Request.TypeParameters
                .Select(parameter => SymbolExtensions.EscapeIdentifier(parameter.Name)))}>";
        }
        else
        {
            methodGenerics = string.Empty;
        }

        // Value parameter names must not collide with the request's type parameter names (CS0412).
        // The semantic names are compared with ordinal semantics, independent of '@' escaping.
        var occupied = new HashSet<string>(StringComparer.Ordinal);
        foreach (var typeParameter in method.Request.TypeParameters)
        {
            occupied.Add(typeParameter.Name);
        }

        var senderParameter = AllocateParameterName(occupied, "sender");
        var requestParameter = AllocateParameterName(occupied, "request");
        var cancellationTokenParameter = AllocateParameterName(occupied, "cancellationToken");

        writer.WriteLine($"/// <summary>Source-generated overload - delegates to <c>{targetMethodName}&lt;{EscapeXml(method.Request.Name)}, {EscapeXml(method.Response.Name)}&gt;</c>.</summary>");
        writer.WriteLine("[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
        writer.WriteLine($"{visibility} static {returnType}{methodName}{methodGenerics}(this {senderTypeFqn} {senderParameter}, {requestRef} {requestParameter}, global::System.Threading.CancellationToken {cancellationTokenParameter} = default)");
        using (writer.Indent())
        {
            WriteConstraints(writer, method.Request);
            writer.WriteLine($"=> {senderParameter}.{targetMethodName}<{requestRef}, {responseRef}>({requestParameter}, {cancellationTokenParameter});");
        }
    }

    private static string AllocateParameterName(HashSet<string> occupied, string baseName)
    {
        var candidate = baseName;

        for (var suffix = 1; !occupied.Add(candidate); suffix++)
        {
            candidate = baseName + suffix;
        }

        return candidate;
    }

    private static void WriteConstraints(CodeWriter writer, INamedTypeSymbol type)
    {
        foreach (var typeParameter in type.TypeParameters)
        {
            var parts = new List<string>();

            if (typeParameter.HasReferenceTypeConstraint)
            {
                parts.Add(typeParameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            }
            else if (typeParameter.HasUnmanagedTypeConstraint) parts.Add("unmanaged");
            else if (typeParameter.HasValueTypeConstraint) parts.Add("struct");
            else if (typeParameter.HasNotNullConstraint) parts.Add("notnull");

            foreach (var constraint in typeParameter.ConstraintTypes)
            {
                parts.Add(constraint.ToFqn());
            }

            if (typeParameter.HasConstructorConstraint) parts.Add("new()");

            if (parts.Count == 0) continue;

            writer.WriteLine($"where {SymbolExtensions.EscapeIdentifier(typeParameter.Name)} : {string.Join(", ", parts)}");
        }
    }

    private static string LoadTemplate()
    {
        using var stream = typeof(SenderExtensionsGenerator).Assembly.GetManifestResourceStream(TemplateResource)
            ?? throw new InvalidOperationException($"Embedded source template '{TemplateResource}' was not found.");

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        return reader.ReadToEnd();
    }

    private static string ReplaceRequiredToken(string template, string token, string value)
    {
        if (template.IndexOf(token, StringComparison.Ordinal) < 0)
        {
            throw new InvalidOperationException($"Source template token '{token}' was not found.");
        }

        return template.Replace(token, value);
    }

    private static string ReplaceIndentedToken(string template, string token, string value)
    {
        var tokenIndex = template.IndexOf(token, StringComparison.Ordinal);
        if (tokenIndex < 0)
        {
            throw new InvalidOperationException($"Source template token '{token}' was not found.");
        }

        var lineStart = template.LastIndexOf('\n', tokenIndex);
        lineStart = lineStart < 0 ? 0 : lineStart + 1;

        var indentation = template.Substring(lineStart, tokenIndex - lineStart);
        if (indentation.Any(character => character is not (' ' or '\t')))
        {
            throw new InvalidOperationException($"Source template token '{token}' must be the only content on its line.");
        }

        var tokenEnd = tokenIndex + token.Length;
        var lineEnd = template.IndexOf('\n', tokenEnd);
        var contentEnd = lineEnd < 0 ? template.Length : lineEnd;
        if (contentEnd > tokenEnd && template[contentEnd - 1] == '\r')
        {
            contentEnd--;
        }

        if (!string.IsNullOrWhiteSpace(template.Substring(tokenEnd, contentEnd - tokenEnd)))
        {
            throw new InvalidOperationException($"Source template token '{token}' must be the only content on its line.");
        }

        var indentedValue = indentation + value.Replace("\n", "\n" + indentation);

        return template.Substring(0, lineStart) + indentedValue + template.Substring(contentEnd);
    }

    private static string EscapeXml(string value)
    {
        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}