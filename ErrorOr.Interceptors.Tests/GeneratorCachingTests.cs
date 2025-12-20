using ANcpLua.Interceptors.ErrorOr.Generator.Interceptors;
using Xunit;

namespace ErrorOr.Interceptors.Tests;

public class GeneratorCachingTests
{
    private const string Usings = """
                                  using ErrorOr;
                                  using Microsoft.AspNetCore.Builder;
                                  """;

    private const string AppSetup = """
                                    var builder = WebApplication.CreateBuilder();
                                    var app = builder.Build();
                                    """;

    private const string UnrelatedFile = """
                                         // Unrelated file - should not cause regeneration
                                         public class UnrelatedService
                                         {
                                             public void DoSomething() { }
                                         }
                                         """;

    [Fact]
    public async Task Generator_WhenSourceUnchanged_Caches()
    {
        await $$"""
                {{Usings}}

                {{AppSetup}}

                app.MapGet("/users/{id}", ErrorOr<User> (int id) => id == 0
                    ? Error.NotFound("User.NotFound", "User not found")
                    : new User(id, "Test"));

                public record User(int Id, string Name);
                """.ShouldCache<ErrorOrInterceptorGenerator>(["Configuration", "CallSites", "GroupedCalls", "FinalOutput"]);
    }

    [Fact]
    public async Task Generator_WhenEndpointAdded_Regenerates()
    {
        await $$"""
                {{Usings}}

                {{AppSetup}}

                app.MapGet("/test", ErrorOr<string> () => "ok");
                """.ShouldRegenerate<ErrorOrInterceptorGenerator>($$"""
                                                                 {{Usings}}

                                                                 {{AppSetup}}

                                                                 app.MapGet("/test", ErrorOr<string> () => "ok");

                                                                 app.MapPost("/test", ErrorOr<string> (string input) => string.IsNullOrEmpty(input)
                                                                     ? Error.Validation("Input.Required", "Input required")
                                                                     : input);
                                                                 """);
    }

    [Fact]
    public async Task Generator_WhenUnrelatedFileAdded_DoesNotRegenerate()
    {
        await $$"""
                {{Usings}}

                {{AppSetup}}

                app.MapGet("/stable", ErrorOr<int> () => 42);
                """.ShouldNotRegenerate<ErrorOrInterceptorGenerator>(UnrelatedFile);
    }
}
