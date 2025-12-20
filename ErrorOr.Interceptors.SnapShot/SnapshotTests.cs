using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using ANcpLua.Interceptors.ErrorOr.Generator.Interceptors;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace ErrorOr.Interceptors.SnapShot;

public class SnapshotTests
{
    private const string Usings = """
                                  using ErrorOr;
                                  using Microsoft.AspNetCore.Builder;
                                  """;

    private const string AppSetup = """
                                    var builder = WebApplication.CreateBuilder();
                                    var app = builder.Build();
                                    """;

    // Note: Net100 not yet available in Microsoft.CodeAnalysis.Testing.ReferenceAssemblies
    // Using Net90 until .NET 10 GA when the package is updated
    private static readonly ReferenceAssemblies References = ReferenceAssemblies.Net.Net90
        .AddPackages([
            new PackageIdentity("ErrorOr", "2.0.1"),
            new PackageIdentity("Microsoft.AspNetCore.App.Ref", "9.0.0")
        ]);

    [ModuleInitializer]
    [SuppressMessage("Usage", "xUnit1013:Public method should be marked as test",
        Justification = "ModuleInitializer for Verify.SourceGenerators")]
    public static void Init()
    {
        VerifySourceGenerators.Initialize();
        // Ensure snapshot files are created in the source directory
        Verifier.DerivePathInfo((file, _, type, method) => new(
            Path.GetDirectoryName(file)!,
            type.Name,
            method.Name));
    }

    [Fact]
    public Task SyncHandler_InferredErrors()
    {
        const string source = $$"""
                                {{Usings}}

                                {{AppSetup}}

                                app.MapGet("/users/{id}", ErrorOr<User> (int id) => id switch
                                {
                                    < 0 => Error.Validation("User.InvalidId", "Invalid ID"),
                                    0 => Error.NotFound("User.NotFound", "User not found"),
                                    _ => new User(id, "Test")
                                });

                                public record User(int Id, string Name);
                                """;

        return VerifyGenerator(source);
    }

    [Fact]
    public Task AsyncHandler_TaskOfErrorOr()
    {
        const string source = $$"""
                                {{Usings}}
                                using System.Threading.Tasks;

                                {{AppSetup}}

                                app.MapGet("/users/{id}/orders", async Task<ErrorOr<Order>> (int id) =>
                                {
                                    await Task.Delay(1);
                                    if (id == 0)
                                        return Error.NotFound("Order.NotFound", "Order not found");
                                    return new Order(id);
                                });

                                public record Order(int Id);
                                """;

        return VerifyGenerator(source);
    }

    [Fact]
    public Task MethodGroup_Reference()
    {
        const string source = $$"""
                                {{Usings}}

                                {{AppSetup}}

                                app.MapGet("/health", GetHealth);

                                static ErrorOr<string> GetHealth() =>
                                    Error.Unexpected("Health.Failed", "Service unavailable");
                                """;

        return VerifyGenerator(source);
    }

    [Fact]
    public Task MultipleEndpoints_SameSignature_GroupsInterceptors()
    {
        const string source = $$"""
                                {{Usings}}

                                {{AppSetup}}

                                app.MapGet("/a/{id}", ErrorOr<User> (int id) => id == 0
                                    ? Error.NotFound("User.NotFound", "User not found")
                                    : new User(id, "A"));

                                app.MapGet("/b/{id}", ErrorOr<User> (int id) => id == 0
                                    ? Error.NotFound("User.NotFound", "User not found")
                                    : new User(id, "B"));

                                public record User(int Id, string Name);
                                """;

        return VerifyGenerator(source);
    }

    [Fact]
    public Task NoContentTypes_DeletedAndSuccess()
    {
        const string source = $$"""
                                {{Usings}}

                                {{AppSetup}}

                                app.MapDelete("/users/{id}", ErrorOr<Deleted> (int id) => id == 0
                                    ? Error.NotFound("User.NotFound", "User not found")
                                    : Result.Deleted);

                                app.MapPost("/users/{id}/activate", ErrorOr<Success> (int id) => id == 0
                                    ? Error.Validation("User.Invalid", "Invalid ID")
                                    : Result.Success);
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
        var generator = new ErrorOrInterceptorGenerator();
        return CSharpGeneratorDriver.Create([generator.AsSourceGenerator()], parseOptions: parseOptions);
    }

    private static async Task<Compilation> CreateCompilationAsync(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        // Use only NuGet reference assemblies - don't mix with runtime assemblies
        var resolved = await References.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);

        return CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            resolved,
            // Use Exe for top-level statements
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
    }
}
