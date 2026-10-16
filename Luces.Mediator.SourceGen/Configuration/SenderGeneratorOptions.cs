using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Globalization;
using System.Linq;

namespace Luces.Mediator.SourceGen.Configuration;

internal readonly record struct SenderGeneratorOptions(
    bool Enabled,
    string SenderInterfaceName,
    string RequestInterfaceName,
    string StreamRequestInterfaceName,
    string? OutputNamespaceOverride,
    string ClassName,
    string SendMethodName,
    string StreamMethodName)
{
    private const string EnabledProperty = "build_property.LucesMediatorExtensionsEnabled";
    private const string SenderTypeNameProperty = "build_property.LucesMediatorExtensionsSenderType";
    private const string RequestTypeNameProperty = "build_property.LucesMediatorExtensionsRequestType";
    private const string StreamTypeNameProperty = "build_property.LucesMediatorExtensionsStreamTypeName";
    private const string OutputNamespaceProperty = "build_property.LucesMediatorExtensionsNamespace";
    private const string OutputClassNameProperty = "build_property.LucesMediatorExtensionsClassName";
    private const string SendMethodNameProperty = "build_property.LucesMediatorExtensionsSendMethod";
    private const string StreamMethodNameProperty = "build_property.LucesMediatorExtensionsStreamMethod";

    private const string DefaultSenderName = "Luces.Mediator.ISender";
    private const string DefaultRequestName = "Luces.Mediator.IRequest";
    private const string DefaultStreamName = "Luces.Mediator.IStreamRequest";
    private const string DefaultClassName = "SenderExtensions";
    private const string DefaultSendMethod = "Send";
    private const string DefaultStreamMethod = "CreateStream";

    public static SenderGeneratorOptions Read(AnalyzerConfigOptions options)
    {
        bool enabled = options.TryGetValue(EnabledProperty, out var enableValue) && bool.TryParse(enableValue, out var parsedEnabled) && parsedEnabled;
        var outputNameSpace = options.TryGetValue(OutputNamespaceProperty, out var configuredNamespace) && !string.IsNullOrWhiteSpace(configuredNamespace) ? configuredNamespace.Trim() : null;

        return new SenderGeneratorOptions(enabled,
            ReadOrDefault(options, SenderTypeNameProperty, DefaultSenderName),
            ReadOrDefault(options, RequestTypeNameProperty, DefaultRequestName),
            ReadOrDefault(options, StreamTypeNameProperty, DefaultStreamName),
            outputNameSpace,
            ReadOrDefault(options, OutputClassNameProperty, DefaultClassName),
            ReadOrDefault(options, SendMethodNameProperty, DefaultSendMethod),
            ReadOrDefault(options, StreamMethodNameProperty, DefaultStreamMethod)
            );

    }

    public bool Validate(SourceProductionContext context)
    {
        // Interface metadata names accept namespaces, nested-type ('+') separators and an optional
        // arity suffix; class and method names must be a single plain identifier. Each category gets
        // its own validation so metadata rules are never applied to plain identifiers.
        var valid = ValidateInterfaceName(context, SenderTypeNameProperty, SenderInterfaceName, expectedArity: 0)
            & ValidateInterfaceName(context, RequestTypeNameProperty, RequestInterfaceName, expectedArity: 1)
            & ValidateInterfaceName(context, StreamTypeNameProperty, StreamRequestInterfaceName, expectedArity: 1)
            & ValidateSimpleIdentifier(context, OutputClassNameProperty, ClassName)
            & ValidateSimpleIdentifier(context, SendMethodNameProperty, SendMethodName)
            & ValidateSimpleIdentifier(context, StreamMethodNameProperty, StreamMethodName);

        if (OutputNamespaceOverride is not null)
        {
            valid &= ValidateNamespace(context, OutputNamespaceOverride);
        }

        // Only check the class/method combination once each individual value is known to be valid.
        if (valid && !ValidateClassMethodNameCompatibility(context))
        {
            valid = false;
        }

        return valid;
    }

    private static string ReadOrDefault(AnalyzerConfigOptions options, string key, string defaultValue)
    {
        return options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : defaultValue;
    }

    private static bool ValidateSimpleIdentifier(SourceProductionContext context, string property, string value)
    {
        var valid = IsValidSimpleIdentifier(value);

        if (!valid)
        {
            context.ReportDiagnostic(Diagnostic.Create(SenderGeneratorDiagnostics.InvalidOption, Location.None, property, value));
        }

        return valid;
    }

    private static bool ValidateInterfaceName(SourceProductionContext context, string property, string value, int expectedArity)
    {
        var valid = IsValidInterfaceName(value, expectedArity);

        if (!valid)
        {
            context.ReportDiagnostic(Diagnostic.Create(SenderGeneratorDiagnostics.InvalidOption, Location.None, property, value));
        }

        return valid;
    }

    private static bool ValidateNamespace(SourceProductionContext context, string value)
    {
        var valid = IsValidNamespace(value);

        if (!valid)
        {
            context.ReportDiagnostic(Diagnostic.Create(SenderGeneratorDiagnostics.InvalidOption, Location.None, OutputNamespaceProperty, value));
        }

        return valid;
    }

    private bool ValidateClassMethodNameCompatibility(SourceProductionContext context)
    {
        // A class cannot contain a member with the same name (CS0542), so reject the combination
        // before it can ever reach the generated source. Comparison is ordinal (case-sensitive).
        var valid = true;

        if (string.Equals(ClassName, SendMethodName, StringComparison.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(SenderGeneratorDiagnostics.ExtensionClassMethodNameConflict, Location.None, OutputClassNameProperty, ClassName, SendMethodNameProperty, SendMethodName));
            valid = false;
        }

        if (string.Equals(ClassName, StreamMethodName, StringComparison.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(SenderGeneratorDiagnostics.ExtensionClassMethodNameConflict, Location.None, OutputClassNameProperty, ClassName, StreamMethodNameProperty, StreamMethodName));
            valid = false;
        }

        return valid;
    }

    // A plain identifier: no qualification, nested separator or generic arity, and not a reserved keyword.
    private static readonly char[] DisallowedIdentifierCharacters = { '.', '+', '`' };

    private static bool IsValidSimpleIdentifier(string value)
    {
        return value.IndexOfAny(DisallowedIdentifierCharacters) < 0
            && SyntaxFacts.IsValidIdentifier(value)
            && SyntaxFacts.GetKeywordKind(value) == SyntaxKind.None;
    }

    private static bool IsValidInterfaceName(string value, int expectedArity)
    {
        var name = value.StartsWith("global::", StringComparison.Ordinal) ? value.Substring("global::".Length) : value;

        // Metadata names combine namespace separators ('.') with nested-type separators ('+'); each
        // segment may carry an optional '`arity' suffix whose value must match the interface arity.
        var segments = name.Split('.', '+');

        foreach (var segment in segments)
        {
            if (segment.Length == 0) return false;

            var arityIndex = segment.IndexOf('`');
            if (arityIndex < 0)
            {
                if (!IsValidSimpleIdentifier(segment)) return false;
                continue;
            }

            var identifier = segment.Substring(0, arityIndex);
            var arityText = segment.Substring(arityIndex + 1);

            if (!IsValidSimpleIdentifier(identifier)) return false;
            if (!int.TryParse(arityText, NumberStyles.None, CultureInfo.InvariantCulture, out var arity) || arity != expectedArity) return false;
        }

        return segments.Length > 0;
    }

    private static bool IsValidNamespace(string value)
    {
        var segments = value.Split('.');

        return segments.Length > 0 && segments.All(IsValidSimpleIdentifier);
    }
}