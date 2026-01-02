using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr.Http.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyTests;
using Xunit;

namespace ErrorOr.Http.SnapShot;

public class SnapshotTests
{
    private const string Usings = """
                                  using ErrorOr;
                                  using ErrorOr.Http;
                                  """;


    private static readonly ReferenceAssemblies s_references = ReferenceAssemblies.Net.Net90
        .AddPackages([
            new PackageIdentity("ErrorOr", "2.0.1"),
            new PackageIdentity("Microsoft.AspNetCore.App.Ref", "10.0.1")
        ]);

    [ModuleInitializer]
    [SuppressMessage("Usage", "xUnit1013:Public method should be marked as test",
        Justification = "ModuleInitializer for Verify.SourceGenerators")]
    public static void Init()
    {
        VerifySourceGenerators.Initialize();

        DerivePathInfo((file, _, type, method) => new PathInfo(
            Path.GetDirectoryName(file)!,
            type.Name,
            method.Name));
    }

    [Fact]
    public Task SyncHandler_InferredErrors()
    {
        const string Source = $$"""
                                {{Usings}}

                                public static class Endpoints
                                {
                                    [ErrorOrEndpoint("GET", "/users/{id}")]
                                    public static ErrorOr<User> GetUser(int id) => id switch
                                    {
                                        < 0 => Error.Validation("User.InvalidId", "Invalid ID"),
                                        0 => Error.NotFound("User.NotFound", "User not found"),
                                        _ => new User(id, "Test")
                                    };
                                }

                                public record User(int Id, string Name);
                                """;

        return VerifyGenerator(Source);
    }

    [Fact]
    public Task AsyncHandler_TaskOfErrorOr()
    {
        const string Source = $$"""
                                {{Usings}}
                                using System.Threading.Tasks;

                                public static class Endpoints
                                {
                                    [ErrorOrEndpoint("GET", "/users/{id}/orders")]
                                    public static async Task<ErrorOr<Order>> GetOrders(int id)
                                    {
                                        await Task.Delay(1);
                                        if (id is 0)
                                            return Error.NotFound("Order.NotFound", "Order not found");
                                        return new Order(id);
                                    }
                                }

                                public record Order(int Id);
                                """;

        return VerifyGenerator(Source);
    }

    [Fact]
    public Task MethodGroup_Reference()
    {
        const string Source = $$"""
                                {{Usings}}

                                public static class Endpoints
                                {
                                    [ErrorOrEndpoint("GET", "/health")]
                                    public static ErrorOr<string> GetHealth() =>
                                        Error.Unexpected("Health.Failed", "Service unavailable");
                                }
                                """;

        return VerifyGenerator(Source);
    }

    [Fact]
    public Task MultipleEndpoints_SameSignature_GroupsEndpoints()
    {
        const string Source = $$"""
                                {{Usings}}

                                public static class Endpoints
                                {
                                    [ErrorOrEndpoint("GET", "/a/{id}")]
                                    [ErrorOrEndpoint("GET", "/b/{id}")]
                                    public static ErrorOr<User> GetUser(int id) => id is 0
                                        ? Error.NotFound("User.NotFound", "User not found")
                                        : new User(id, "Test");
                                }

                                public record User(int Id, string Name);
                                """;

        return VerifyGenerator(Source);
    }

    [Fact]
    public Task NoContentTypes_DeletedAndSuccess()
    {
        const string Source = $$"""
                                {{Usings}}

                                public static class Endpoints
                                {
                                    [ErrorOrEndpoint("DELETE", "/users/{id}")]
                                    public static ErrorOr<Deleted> DeleteUser(int id) => id is 0
                                        ? Error.NotFound("User.NotFound", "User not found")
                                        : Result.Deleted;

                                    [ErrorOrEndpoint("POST", "/users/{id}/activate")]
                                    public static ErrorOr<Success> ActivateUser(int id) => id is 0
                                        ? Error.Validation("User.Invalid", "Invalid ID")
                                        : Result.Success;
                                }
                                """;

        return VerifyGenerator(Source);
    }

    [Fact]
    public Task AsParameters_Recursive()
    {
        const string Source = $$"""
                                {{Usings}}
                                using Microsoft.AspNetCore.Http;
                                using Microsoft.AspNetCore.Mvc;

                                public static class Endpoints
                                {
                                    [ErrorOrEndpoint("GET", "/search")]
                                    public static ErrorOr<string> Search([AsParameters] SearchRequest request) => 
                                        $"Query: {request.Query}, Page: {request.Page}, Header: {request.ApiKey}";
                                }

                                public record SearchRequest(
                                    [FromQuery] string Query,
                                    [FromHeader(Name = "X-Api-Key")] string ApiKey,
                                    int Page = 1);
                                """;

        return VerifyGenerator(Source);
    }

    [Fact]
    public Task Headers_And_Collections()
    {
        const string Source = $$"""
                                {{Usings}}
                                using Microsoft.AspNetCore.Mvc;
                                using System.Collections.Generic;

                                public static class Endpoints
                                {
                                    [ErrorOrEndpoint("GET", "/tags")]
                                    public static ErrorOr<string> GetTags(
                                        [FromHeader] string region,
                                        [FromQuery] List<int> ids,
                                        string[] categories) // Implicit query collection
                                    {
                                        return "tags";
                                    }
                                }
                                """;

        return VerifyGenerator(Source);
    }

    [Fact]
    public Task FormBinding_PrimitiveAndFile()
    {
        var source = """
            using ErrorOr;
            using ErrorOr.Http;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Mvc;

            public static class Handlers
            {
                [ErrorOrEndpoint("POST", "/upload")]
                public static ErrorOr<Success> Upload(
                    [FromForm] string title,
                    [FromForm] int version,
                    IFormFile document)
                    => Result.Success;
            }
            """;

        return VerifyGenerator(source);
    }

    private static async Task VerifyGenerator(string source)
    {
        var driver = CreateDriver();
        var compilation = await CreateCompilationAsync(source);
        var results = driver.RunGenerators(compilation).GetRunResult();
        await Verify(results);
    }

    private static GeneratorDriver CreateDriver()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var generator = new ErrorOrEndpointGenerator();
        return CSharpGeneratorDriver.Create([generator.AsSourceGenerator()], parseOptions: parseOptions);
    }

    private static async Task<Compilation> CreateCompilationAsync(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var resolved = await s_references.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);

        return CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            resolved,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
    }
}
