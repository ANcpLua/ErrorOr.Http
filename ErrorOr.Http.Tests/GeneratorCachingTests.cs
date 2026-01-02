using ANcpLua.Roslyn.Utilities.Testing;
using ErrorOr.Http.Generators;
using Xunit;

namespace ErrorOr.Http.Tests;

[Collection("NonParallelRoslyn")]
public class GeneratorCachingTests
{
    private const string Usings = """
                                  using ErrorOr;
                                  using ErrorOr.Http;
                                  """;

    private const string UnrelatedFile = """
                                         public class UnrelatedService
                                         {
                                             public void DoSomething() { }
                                         }
                                         """;

    private static readonly string[] s_trackingNames = ["EndpointCollection"];

    [Fact]
    public Task Generator_WhenSourceUnchanged_Caches()
    {
        return $$"""
                 {{Usings}}

                 public static class Endpoints
                 {
                     [Get( "/users/{id}")]
                     public static ErrorOr<User> GetUser(int id) => id is 0
                         ? Error.NotFound("User.NotFound", "User not found")
                         : new User(id, "Test");
                 }

                 public record User(int Id, string Name);
                 """.ShouldBeCached<ErrorOrEndpointGenerator>(s_trackingNames);
    }

    [Fact]
    public Task Generator_WhenEndpointAdded_Regenerates()
    {
        return $$"""
                 {{Usings}}

                 public static class Endpoints
                 {
                     [Get( "/test")]
                     public static ErrorOr<string> Get() => "ok";
                 }
                 """.ShouldRegenerate<ErrorOrEndpointGenerator>($$"""
                                                                  {{Usings}}

                                                                  public static class Endpoints
                                                                  {
                                                                      [Get( "/test")]
                                                                      public static ErrorOr<string> Get() => "ok";

                                                                      [Post( "/test")]
                                                                      public static ErrorOr<string> Create(string input) => string.IsNullOrEmpty(input)
                                                                          ? Error.Validation("Input.Required", "Input required")
                                                                          : input;
                                                                  }
                                                                  """);
    }

    [Fact]
    public Task Generator_WhenUnrelatedFileAdded_DoesNotRegenerate()
    {
        return $$"""
                 {{Usings}}

                 public static class Endpoints
                 {
                     [Get( "/stable")]
                     public static ErrorOr<int> Get() => 42;
                 }
                 """.ShouldNotRegenerate<ErrorOrEndpointGenerator>(UnrelatedFile);
    }
}
