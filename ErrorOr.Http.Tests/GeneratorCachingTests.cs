using System.Threading.Tasks;
using ANcpLua.Roslyn.Utilities.Testing;
using ErrorOr.Http.Generators;
using Xunit;

namespace ErrorOr.Http.Tests;

// Same collection as EndpointGeneratorTests - caching tests share Roslyn state
[Collection("NonParallelRoslyn")]
public class GeneratorCachingTests
{
    private const string Usings = """
                                  using ErrorOr;
                                  using ErrorOr.Http;
                                  """;

    private const string UnrelatedFile = """
                                         // Unrelated file - should not cause regeneration
                                         public class UnrelatedService
                                         {
                                             public void DoSomething() { }
                                         }
                                         """;

    private static readonly string[] s_trackingNames = ["EndpointMethods", "EndpointDescriptors", "EndpointCollection"];

    [Fact]
    public async Task Generator_WhenSourceUnchanged_Caches()
    {
        await $$"""
                {{Usings}}

                public static class Endpoints
                {
                    [ErrorOrEndpoint("GET", "/users/{id}")]
                    public static ErrorOr<User> GetUser(int id) => id is 0
                        ? Error.NotFound("User.NotFound", "User not found")
                        : new User(id, "Test");
                }

                public record User(int Id, string Name);
                """.ShouldCache<ErrorOrEndpointGenerator>(s_trackingNames);
    }

    [Fact]
    public async Task Generator_WhenEndpointAdded_Regenerates()
    {
        await $$"""
                {{Usings}}

                public static class Endpoints
                {
                    [ErrorOrEndpoint("GET", "/test")]
                    public static ErrorOr<string> Get() => "ok";
                }
                """.ShouldRegenerate<ErrorOrEndpointGenerator>($$"""
                                                                 {{Usings}}

                                                                 public static class Endpoints
                                                                 {
                                                                     [ErrorOrEndpoint("GET", "/test")]
                                                                     public static ErrorOr<string> Get() => "ok";

                                                                     [ErrorOrEndpoint("POST", "/test")]
                                                                     public static ErrorOr<string> Create(string input) => string.IsNullOrEmpty(input)
                                                                         ? Error.Validation("Input.Required", "Input required")
                                                                         : input;
                                                                 }
                                                                 """);
    }

    [Fact]
    public async Task Generator_WhenUnrelatedFileAdded_DoesNotRegenerate()
    {
        await $$"""
                {{Usings}}

                public static class Endpoints
                {
                    [ErrorOrEndpoint("GET", "/stable")]
                    public static ErrorOr<int> Get() => 42;
                }
                """.ShouldNotRegenerate<ErrorOrEndpointGenerator>(UnrelatedFile);
    }
}
