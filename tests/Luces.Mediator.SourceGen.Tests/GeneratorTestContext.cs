using Luces.Mediator.SourceGen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace Luces.Mediator.SourceGen.Tests;

/// <summary>
/// Drives <see cref="SenderExtensionsGenerator"/> against in-memory sources with simulated
/// MSBuild generator options, mirroring how the Roslyn SDK would host it in a real build.
/// </summary>
public sealed class GeneratorTestContext
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> LazyReferences = new(LoadReferences);

    public GeneratorDriver Driver { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    public ImmutableArray<GeneratedSourceResult> Results { get; }
    public CSharpCompilation InputCompilation { get; }

    private GeneratorTestContext(GeneratorDriver driver, GeneratorDriverRunResult runResult, CSharpCompilation inputCompilation)
    {
        Driver = driver;
        Results = runResult.Results[0].GeneratedSources;
        Diagnostics = runResult.Results[0].Diagnostics;
        InputCompilation = inputCompilation;
    }

    public string? GetGeneratedSource()
    {
        return Results.IsEmpty ? null : Results[0].SourceText.ToString();
    }

    /// <summary>Runs a follow-up compilation that includes the generated source, verifying it compiles.</summary>
    public CSharpCompilation CreateOutputCompilation()
    {
        var generated = GetGeneratedSource()
            ?? throw new InvalidOperationException("No source was generated.");

        return InputCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated, path: "GeneratedSenderExtensions.g.cs"));
    }

    public static GeneratorTestContext Run(
        string source,
        bool coreLibraryReferenced = true,
        IDictionary<string, string>? buildProperties = null)
    {
        var optionsProvider = new TestAnalyzerConfigOptions(buildProperties);
        var references = LazyReferences.Value;
        if (!coreLibraryReferenced)
        {
            references = references.RemoveAll(r => r.Display?.Contains("Luces.Mediator.dll") == true);
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, path: "Test.cs")],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var generator = new SenderExtensionsGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator)
            .WithUpdatedParseOptions(CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest))
            .WithUpdatedAnalyzerConfigOptions(optionsProvider);

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        return new GeneratorTestContext(driver, runResult, compilation);
    }

    private static ImmutableArray<MetadataReference> LoadReferences()
    {
        // The SDK's reference pack contains the contract assemblies the real compiler would
        // compile against — the correct metadata for in-memory compilations in tests.
        var refPack = FindLatestRefPack()
            ?? throw new InvalidOperationException(
                "No Microsoft.NETCore.App.Ref pack was found (searched DOTNET_ROOT, Program Files, /usr/share/dotnet, /usr/lib/dotnet, /usr/lib64/dotnet). Install the .NET SDK or set DOTNET_ROOT.");

        var references = Directory.EnumerateFiles(refPack, "*.dll")
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();

        references = references.Add(MetadataReference.CreateFromFile(typeof(Luces.Mediator.ISender).Assembly.Location));

        return references;
    }

    private static string? FindLatestRefPack()
    {
        foreach (var root in CandidateDotNetRoots())
        {
            if (string.IsNullOrEmpty(root))
                continue;

            var packsRoot = Path.Combine(root, "packs", "Microsoft.NETCore.App.Ref");
            if (!Directory.Exists(packsRoot))
                continue;

            // Pick the newest pack version: compiling against a newer ref pack than a referenced
            // assembly was built with is safe (CS1705 only fires the other way around).
            var pack = Directory.EnumerateDirectories(packsRoot)
                .Select(versionDir => (Path: versionDir, Version: Version.TryParse(Path.GetFileName(versionDir), out var v) ? v : null))
                .Where(entry => entry.Version is not null)
                .OrderByDescending(entry => entry.Version)
                .SelectMany(entry => Directory.EnumerateDirectories(Path.Combine(entry.Path, "ref")))
                .FirstOrDefault(refDir => File.Exists(Path.Combine(refDir, "System.Runtime.dll")));

            if (pack is not null)
                return pack;
        }

        return null;
    }

    private static IEnumerable<string?> CandidateDotNetRoots()
    {
        yield return Environment.GetEnvironmentVariable("DOTNET_ROOT");

        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
        }
        else
        {
            // Typical install roots on Linux distros and CI images.
            yield return "/usr/share/dotnet";
            yield return "/usr/lib/dotnet";
            yield return "/usr/lib64/dotnet";
        }
    }

    private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptionsProvider
    {
        private readonly TestOptions _globalOptions;

        public TestAnalyzerConfigOptions(IDictionary<string, string>? buildProperties)
        {
            _globalOptions = new TestOptions(buildProperties);
        }

        public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _globalOptions;

        private sealed class TestOptions : AnalyzerConfigOptions
        {
            private readonly IReadOnlyDictionary<string, string> _values;

            public TestOptions(IDictionary<string, string>? buildProperties)
            {
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    // The shipped buildTransitive props default the generator to enabled;
                    // mirror that default here unless a test overrides it.
                    ["build_property.LucesMediatorExtensionsEnabled"] = "true",
                };

                if (buildProperties is not null)
                {
                    foreach (var pair in buildProperties)
                    {
                        values["build_property." + pair.Key] = pair.Value;
                    }
                }

                _values = values;
            }

            public override bool TryGetValue(string key, out string value)
            {
                // The base signature carries no nullable annotations, so the override must match
                // `out string` exactly; a miss yields the empty sentinel rather than null.
                if (_values.TryGetValue(key, out var found) && found is not null)
                {
                    value = found;
                    return true;
                }

                value = string.Empty;
                return false;
            }
        }
    }
}
