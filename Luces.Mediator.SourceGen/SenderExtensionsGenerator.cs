using Luces.Mediator.SourceGen.Configuration;
using Luces.Mediator.SourceGen.Discovery;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace Luces.Mediator.SourceGen;

[Generator(LanguageNames.CSharp)]
public class SenderExtensionsGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var optionsProvider = context.AnalyzerConfigOptionsProvider.Select(static (options, _) => SenderGeneratorOptions.Read(options.GlobalOptions));

        var candidates = context.SyntaxProvider.CreateSyntaxProvider(predicate: static (node, _) => SenderExtensionsDiscovery.IsCandidate(node), transform: static (syntaxContext, cancellationToken) =>
                    syntaxContext.SemanticModel.GetDeclaredSymbol((TypeDeclarationSyntax)syntaxContext.Node, cancellationToken) as INamedTypeSymbol).Where(static symbol => symbol is not null).Collect();

        var combined = candidates.Combine(context.CompilationProvider).Combine(optionsProvider);

        context.RegisterSourceOutput(combined, static (productionContext, value) =>
        {
            var ((symbols, compilation), options) = value;
            if (!options.Enabled || !options.Validate(productionContext)) return;

            var model = SenderExtensionsDiscovery.Discover(productionContext, compilation, symbols, options);
            if (model is null) return;

            var source = SenderExtensionsEmitter.Emit(model.Value, options);
            productionContext.AddSource("GeneratedSenderExtensions.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }
}