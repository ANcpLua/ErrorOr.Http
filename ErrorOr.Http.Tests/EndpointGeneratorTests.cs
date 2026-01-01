using System.Threading.Tasks;
using ANcpLua.Roslyn.Utilities.Testing;
using ErrorOr.Http.Generators;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ErrorOr.Http.Tests;

// Disable parallelization - caching tests are sensitive to shared Roslyn compilation state
[Collection("NonParallelRoslyn")]
public class EndpointGeneratorTests
{
    private const string SyncHandlerSource = """
                                             using ErrorOr;
                                             using ErrorOr.Http;

                                             public static class Endpoints
                                             {
                                                 [ErrorOrEndpoint("GET", "/users/{id}")]
                                                 public static ErrorOr<User> GetUser(int id) =>
                                                     id < 0 ? Error.Validation("User.InvalidId", "Invalid ID") :
                                                     id is 0 ? Error.NotFound("User.NotFound", "Not found") :
                                                     new User(id, "Test");
                                             }

                                             public record User(int Id, string Name);
                                             """;

    private const string AsyncHandlerSource = """
                                              using ErrorOr;
                                              using ErrorOr.Http;
                                              using System.Threading.Tasks;

                                              public static class Endpoints
                                              {
                                                  [ErrorOrEndpoint("GET", "/async")]
                                                  public static async Task<ErrorOr<string>> GetAsync()
                                                  {
                                                      await Task.Delay(1);
                                                      return "ok";
                                                  }
                                              }
                                              """;

    private const string DeleteNoContentSource = """
                                                 using ErrorOr;
                                                 using ErrorOr.Http;

                                                 public static class Endpoints
                                                 {
                                                     [ErrorOrEndpoint("DELETE", "/users/{id}")]
                                                     public static ErrorOr<Deleted> Delete(int id) =>
                                                         id is 0 ? Error.NotFound("User.NotFound", "Not found") : Result.Deleted;
                                                 }
                                                 """;

    private const string MultipleAttributesSource = """
                                                    using ErrorOr;
                                                    using ErrorOr.Http;

                                                    public static class Endpoints
                                                    {
                                                        [ErrorOrEndpoint("GET", "/a")]
                                                        [ErrorOrEndpoint("GET", "/b")]
                                                        public static ErrorOr<string> Get() => "ok";
                                                    }
                                                    """;

    private const string FromRouteNamedSource = """
                                                using ErrorOr;
                                                using ErrorOr.Http;
                                                using Microsoft.AspNetCore.Mvc;

                                                public static class Endpoints
                                                {
                                                    [ErrorOrEndpoint("GET", "/users/{userId}")]
                                                    public static ErrorOr<string> GetUser([FromRoute(Name = "userId")] int id) => "ok";
                                                }
                                                """;

    private const string AmbiguousParameterSource = """
                                                    using ErrorOr;
                                                    using ErrorOr.Http;

                                                    public static class Endpoints
                                                    {
                                                        [ErrorOrEndpoint("POST", "/users")]
                                                        public static ErrorOr<string> CreateUser(User name, User email) => "ok";
                                                    }

                                                    public record User(string Name);
                                                    """;

    private const string MultipleBodyParametersSource = """
                                                        using ErrorOr;
                                                        using ErrorOr.Http;
                                                        using Microsoft.AspNetCore.Mvc;

                                                        public static class Endpoints
                                                        {
                                                            [ErrorOrEndpoint("POST", "/users")]
                                                            public static ErrorOr<string> CreateUser([FromBody] string name, [FromBody] string email) => "ok";
                                                        }
                                                        """;


    private const string IndirectErrorSource = """
                                               using ErrorOr;
                                               using ErrorOr.Http;

                                               public static class Endpoints
                                               {
                                                   private static Error UserNotFound => Error.NotFound("User.NotFound", "Not found");

                                                   [ErrorOrEndpoint("GET", "/users/{id}")]
                                                   public static ErrorOr<string> GetUser(int id) => UserNotFound;
                                               }
                                               """;


    private static readonly string[] s_trackingNames = ["EndpointMethods", "EndpointDescriptors", "EndpointCollection"];


    [Fact]
    public Task SyncHandler_Compiles()
    {
        return SyncHandlerSource.ShouldCompile<ErrorOrEndpointGenerator>();
    }

    [Fact]
    public Task AsyncHandler_Compiles()
    {
        return AsyncHandlerSource.ShouldCompile<ErrorOrEndpointGenerator>();
    }

    [Fact]
    public Task DeleteNoContent_Compiles()
    {
        return DeleteNoContentSource.ShouldCompile<ErrorOrEndpointGenerator>();
    }

    [Fact]
    public Task FromRouteNamed_Compiles()
    {
        return FromRouteNamedSource.ShouldCompile<ErrorOrEndpointGenerator>();
    }


    [Fact]
    public Task SyncHandler_GeneratesMapMethods()
    {
        return SyncHandlerSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            "app.MapMethods(@\"/users/{id}\", new[] { \"GET\" }, (RequestDelegate)Invoke_",
            false);
    }

    [Fact]
    public Task SyncHandler_GeneratesProducesMetadata()
    {
        return SyncHandlerSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            ".WithMetadata(new global::Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute(typeof(global::User), 200))",
            false);
    }

    [Fact]
    public Task SyncHandler_GeneratesProducesProblems()
    {
        return SyncHandlerSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            "typeof(global::Microsoft.AspNetCore.Http.HttpValidationProblemDetails), 400",
            false);
    }

    [Fact]
    public Task SyncHandler_GeneratesProducesNotFound()
    {
        return SyncHandlerSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            "typeof(global::Microsoft.AspNetCore.Mvc.ProblemDetails), 404",
            false);
    }

    [Fact]
    public Task DeleteNoContent_GeneratesNoContentResponse()
    {
        return DeleteNoContentSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            "TypedResults.NoContent()",
            false);
    }

    [Fact]
    public Task DeleteNoContent_GeneratesNoContentMetadata()
    {
        return DeleteNoContentSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            ".WithMetadata(new global::Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute(204))",
            false);
    }

    [Fact]
    public Task MultipleAttributes_GeneratesFirstRoute()
    {
        return MultipleAttributesSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            "app.MapMethods(@\"/a\", new[] { \"GET\" }, (RequestDelegate)Invoke_",
            false);
    }

    [Fact]
    public Task MultipleAttributes_GeneratesSecondRoute()
    {
        return MultipleAttributesSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            "app.MapMethods(@\"/b\", new[] { \"GET\" }, (RequestDelegate)Invoke_",
            false);
    }

    [Fact]
    public Task FromRouteNamed_GeneratesCorrectBinding()
    {
        return FromRouteNamedSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            "if (!TryGetRouteValue(ctx, \"userId\", out var p0Raw)",
            false);
    }

    [Fact]
    public Task SyncHandler_CachesOnUnchangedSource()
    {
        return SyncHandlerSource.ShouldBeCached<ErrorOrEndpointGenerator>(s_trackingNames);
    }

    [Fact]
    public Task MultipleAttributes_CachesOnUnchangedSource()
    {
        return MultipleAttributesSource.ShouldBeCached<ErrorOrEndpointGenerator>(s_trackingNames);
    }


    [Fact]
    public Task SyncHandler_NoDiagnostics()
    {
        return SyncHandlerSource.ShouldHaveNoDiagnostics<ErrorOrEndpointGenerator>();
    }

    [Fact]
    public Task AsyncHandler_NoDiagnostics()
    {
        return AsyncHandlerSource.ShouldHaveNoDiagnostics<ErrorOrEndpointGenerator>();
    }

    [Fact]
    public Task AmbiguousParameter_ReportsWarning()
    {
        return AmbiguousParameterSource.ShouldHaveDiagnostics<ErrorOrEndpointGenerator>(
            GeneratorTestExtensions.Diagnostic("EOE004", DiagnosticSeverity.Warning),
            GeneratorTestExtensions.Diagnostic("EOE004", DiagnosticSeverity.Warning));
    }

    [Fact]
    public Task MultipleBodyParameters_ReportsError()
    {
        return MultipleBodyParametersSource.ShouldHaveDiagnostics<ErrorOrEndpointGenerator>(
            GeneratorTestExtensions.Diagnostic("EOE005"));
    }

    [Fact]
    public Task IndirectError_GeneratesProducesNotFound()
    {
        return IndirectErrorSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            "typeof(global::Microsoft.AspNetCore.Mvc.ProblemDetails), 404",
            false);
    }
}
