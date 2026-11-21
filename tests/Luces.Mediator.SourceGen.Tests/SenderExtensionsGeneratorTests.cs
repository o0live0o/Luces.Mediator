using Microsoft.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Luces.Mediator.SourceGen.Tests;

public sealed class SenderExtensionsGeneratorTests
{
    private const string DefaultRequests = """
        using Luces.Mediator;

        public sealed record Ping(string Name) : IRequest<string>;
        public sealed record Pong : IRequest<Unit>;
        public sealed record StreamQuery(int Count) : IStreamRequest<int>;
        """;

    [Fact]
    public void Generates_overloads_for_requests_and_stream_requests()
    {
        var result = GeneratorTestContext.Run(DefaultRequests);

        var source = result.GetGeneratedSource();
        Assert.NotNull(source);
        Assert.Contains("public static global::System.Threading.Tasks.ValueTask<string>Send(this global::Luces.Mediator.ISender sender, global::Ping request, global::System.Threading.CancellationToken cancellationToken = default)", source);
        Assert.Contains("public static global::System.Threading.Tasks.ValueTask<global::Luces.Mediator.Unit>Send(this global::Luces.Mediator.ISender sender, global::Pong request, global::System.Threading.CancellationToken cancellationToken = default)", source);
        Assert.Contains("IAsyncEnumerable<int>", source);
        Assert.Contains("CreateStream<global::StreamQuery, int>", source);
    }

    [Fact]
    public void Generated_source_compiles()
    {
        var result = GeneratorTestContext.Run(DefaultRequests);

        var output = result.CreateOutputCompilation();
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void Generated_extension_is_invokable_with_inferred_response_type()
    {
        var result = GeneratorTestContext.Run(DefaultRequests);

        var output = result.CreateOutputCompilation()
            .AddSyntaxTrees(ParseConsumer(""""
            using Luces.Mediator;

            public static class Consumer
            {
                public static global::System.Threading.Tasks.ValueTask<string> Call(global::Luces.Mediator.ISender sender)
                    => sender.Send(new Ping("Ada"));
            }
            """"));

        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    private static Microsoft.CodeAnalysis.SyntaxTree ParseConsumer(string text)
        => Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(text, path: "Consumer.cs");

    [Fact]
    public void No_source_is_generated_without_any_requests()
    {
        var result = GeneratorTestContext.Run("public class Unrelated { }");

        Assert.Null(result.GetGeneratedSource());
    }

    [Fact]
    public void No_source_is_generated_when_generator_is_disabled()
    {
        var result = GeneratorTestContext.Run(DefaultRequests, buildProperties: new Dictionary<string, string>
        {
            ["LucesMediatorExtensionsEnabled"] = "false"
        });

        Assert.Null(result.GetGeneratedSource());
    }

    [Fact]
    public void Sender_not_found_reports_MED002_and_generates_nothing()
    {
        var result = GeneratorTestContext.Run(
            """
            using Luces.Mediator;

            public sealed record Ping(string Name) : IRequest<string>;
            """,
            coreLibraryReferenced: false);

        Assert.Null(result.GetGeneratedSource());
        Assert.Contains(result.Diagnostics, d => d.Id == "MED002");
    }

    [Fact]
    public void Skips_abstract_static_private_and_file_local_request_types()
    {
        var result = GeneratorTestContext.Run(
            """
            using Luces.Mediator;

            public abstract record BaseRequest : IRequest<string>;
            public static record StaticRequest;
            file record FileLocal : IRequest<string>;
            public class Container
            {
                private sealed record Hidden : IRequest<string>;
            }
            """);

        Assert.Null(result.GetGeneratedSource());
    }

    [Fact]
    public void Skips_requests_nested_inside_generic_types()
    {
        var result = GeneratorTestContext.Run(
            """
            using Luces.Mediator;

            public class Wrapper<T>
            {
                public sealed record Inner : IRequest<string>;
            }
            """);

        Assert.Null(result.GetGeneratedSource());
    }

    [Fact]
    public void Multiple_response_types_report_MED001()
    {
        var result = GeneratorTestContext.Run(
            """
            using Luces.Mediator;

            public interface ICustomA : IRequest<string> { }
            public interface ICustomB : IRequest<int> { }
            public sealed record Dual : ICustomA, ICustomB;
            """);

        var source = result.GetGeneratedSource();
        Assert.Contains(result.Diagnostics, d => d.Id == "MED001");
        Assert.Null(source);
    }

    [Fact]
    public void Duplicate_response_types_collapse_into_a_single_overload()
    {
        var result = GeneratorTestContext.Run(
            """
            using Luces.Mediator;

            public interface IStringA : IRequest<string> { }
            public interface IStringB : IRequest<string> { }
            public sealed record Same : IStringA, IStringB;
            """);

        var source = result.GetGeneratedSource();
        Assert.NotNull(source);
        Assert.Single(Regex.Matches(source, "Same request"));
    }

    [Fact]
    public void Request_implementing_both_send_and_stream_reports_MED007_when_method_names_collide()
    {
        var result = GeneratorTestContext.Run(
            """
            using Luces.Mediator;

            public sealed record Both : IRequest<string>, IStreamRequest<string>;
            """,
            buildProperties: new Dictionary<string, string>
            {
                ["LucesMediatorExtensionsStreamMethod"] = "Send"
            });

        Assert.Contains(result.Diagnostics, d => d.Id == "MED007");
        var source = result.GetGeneratedSource();
        Assert.NotNull(source);
        // Only the send overload survives; the duplicate stream overload is dropped.
        Assert.Single(Regex.Matches(source, "global::Both request"));
    }

    [Fact]
    public void Custom_namespace_class_and_method_names_are_applied()
    {
        var result = GeneratorTestContext.Run(DefaultRequests, buildProperties: new Dictionary<string, string>
        {
            ["LucesMediatorExtensionsNamespace"] = "MyApp.Generated",
            ["LucesMediatorExtensionsClassName"] = "DispatchExtensions",
            ["LucesMediatorExtensionsSendMethod"] = "Dispatch",
            ["LucesMediatorExtensionsStreamMethod"] = "Stream"
        });

        var source = result.GetGeneratedSource();
        Assert.NotNull(source);
        Assert.Contains("namespace MyApp.Generated", source);
        Assert.Contains("class DispatchExtensions", source);
        Assert.Contains("Dispatch(this global::Luces.Mediator.ISender sender, global::Ping request", source);
        Assert.Contains("Stream(this global::Luces.Mediator.ISender sender, global::StreamQuery request", source);
    }

    [Fact]
    public void Renamed_methods_still_target_the_underlying_Send_and_CreateStream()
    {
        var result = GeneratorTestContext.Run(DefaultRequests, buildProperties: new Dictionary<string, string>
        {
            ["LucesMediatorExtensionsSendMethod"] = "Dispatch",
            ["LucesMediatorExtensionsStreamMethod"] = "Stream"
        });

        var source = result.GetGeneratedSource();
        Assert.Contains("=> sender.Send<global::Ping, string>(request, cancellationToken);", source);
        Assert.Contains("=> sender.CreateStream<global::StreamQuery, int>(request, cancellationToken);", source);
    }

    [Fact]
    public void Internal_request_generates_internal_extension()
    {
        var result = GeneratorTestContext.Run(
            """
            using Luces.Mediator;

            internal sealed record Secret : IRequest<string>;
            """);

        var source = result.GetGeneratedSource();
        Assert.NotNull(source);
        Assert.Contains("internal static global::System.Threading.Tasks.ValueTask<string>Send(this global::Luces.Mediator.ISender sender, global::Secret request", source);
    }

    [Fact]
    public void Generic_request_generates_generic_extension_with_constraints()
    {
        var result = GeneratorTestContext.Run(
            """
            using Luces.Mediator;

            public sealed record Query<T>(T Value) : IRequest<T> where T : class;
            """);

        var source = result.GetGeneratedSource();
        Assert.NotNull(source);
        Assert.Contains("Send<T>(this global::Luces.Mediator.ISender sender, global::Query<T> request", source);
        Assert.Contains("where T : class", source);
    }

    [Fact]
    public void Invalid_class_name_option_reports_MED005()
    {
        var result = GeneratorTestContext.Run(DefaultRequests, buildProperties: new Dictionary<string, string>
        {
            ["LucesMediatorExtensionsClassName"] = "Not Valid!"
        });

        Assert.Contains(result.Diagnostics, d => d.Id == "MED005");
        Assert.Null(result.GetGeneratedSource());
    }

    [Fact]
    public void Class_name_colliding_with_method_name_reports_MED006()
    {
        var result = GeneratorTestContext.Run(DefaultRequests, buildProperties: new Dictionary<string, string>
        {
            ["LucesMediatorExtensionsClassName"] = "Send"
        });

        Assert.Contains(result.Diagnostics, d => d.Id == "MED006");
        Assert.Null(result.GetGeneratedSource());
    }

    [Fact]
    public void Invalid_namespace_reports_MED005()
    {
        var result = GeneratorTestContext.Run(DefaultRequests, buildProperties: new Dictionary<string, string>
        {
            ["LucesMediatorExtensionsNamespace"] = "Not a Namespace"
        });

        Assert.Contains(result.Diagnostics, d => d.Id == "MED005");
        Assert.Null(result.GetGeneratedSource());
    }

    [Fact]
    public void Ambiguous_interface_name_reports_MED003()
    {
        // Unqualified simple names resolve against source-declared types only; two source-level
        // ISender declarations make the configured simple name ambiguous.
        var result = GeneratorTestContext.Run(
            """
            using Luces.Mediator;

            namespace Other
            {
                public interface ISender { }
            }

            namespace Further
            {
                public interface ISender { }
            }

            public sealed record Ping(string Name) : IRequest<string>;
            """,
            buildProperties: new Dictionary<string, string>
            {
                ["LucesMediatorExtensionsSenderType"] = "ISender"
            });

        Assert.Contains(result.Diagnostics, d => d.Id == "MED003");
        Assert.Null(result.GetGeneratedSource());
    }
}
