using ErrorOr.Interceptors;
using Xunit;

namespace ErrorOr.Interceptors.Tests;

public class InterceptorTests
{
    private const string SyncHandlerSource = """
                                             using ErrorOr;
                                             using Microsoft.AspNetCore.Builder;

                                             var app = WebApplication.Create();
                                             app.MapGet("/users/{id}", ErrorOr<User> (int id) =>
                                                 id == 0 ? Error.NotFound("User.NotFound", "Not found") : new User(id, "Test"));

                                             public record User(int Id, string Name);
                                             """;

    private const string AsyncHandlerSource = """
                                              using ErrorOr;
                                              using Microsoft.AspNetCore.Builder;
                                              using System.Threading.Tasks;

                                              var app = WebApplication.Create();
                                              app.MapGet("/async", async Task<ErrorOr<string>> () =>
                                              {
                                                  await Task.Delay(1);
                                                  return "ok";
                                              });
                                              """;

    private const string MethodGroupSource = """
                                             using ErrorOr;
                                             using Microsoft.AspNetCore.Builder;

                                             var app = WebApplication.Create();
                                             app.MapGet("/health", GetHealth);

                                             static ErrorOr<string> GetHealth() => "healthy";
                                             """;

    private const string DeleteNoContentSource = """
                                                 using ErrorOr;
                                                 using Microsoft.AspNetCore.Builder;

                                                 var app = WebApplication.Create();
                                                 app.MapDelete("/users/{id}", ErrorOr<Deleted> (int id) =>
                                                     id == 0 ? Error.NotFound() : Result.Deleted);
                                                 """;

    private const string MultipleEndpointsSource = """
                                                   using ErrorOr;
                                                   using Microsoft.AspNetCore.Builder;

                                                   var app = WebApplication.Create();
                                                   app.MapGet("/a", ErrorOr<string> () => "a");
                                                   app.MapGet("/b", ErrorOr<string> () => "b");
                                                   app.MapGet("/c", ErrorOr<string> () => "c");
                                                   """;

    // ═══════════════════════════════════════════════════════════════════════════════
    // Compilation Tests
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public Task SyncHandler_Compiles() =>
        SyncHandlerSource.ShouldCompile<ErrorOrInterceptorGenerator>();

    [Fact]
    public Task AsyncHandler_Compiles() =>
        AsyncHandlerSource.ShouldCompile<ErrorOrInterceptorGenerator>();

    [Fact]
    public Task MethodGroup_Compiles() =>
        MethodGroupSource.ShouldCompile<ErrorOrInterceptorGenerator>();

    [Fact]
    public Task DeleteNoContent_Compiles() =>
        DeleteNoContentSource.ShouldCompile<ErrorOrInterceptorGenerator>();

    // ═══════════════════════════════════════════════════════════════════════════════
    // Generated Content Tests
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public Task SyncHandler_GeneratesFilterAndMetadata() =>
        SyncHandlerSource.ShouldGenerate<ErrorOrInterceptorGenerator>(
            "ErrorOrInterceptors.g.cs",
            expectedContent: ".AddEndpointFilter(",
            exactMatch: false);

    [Fact]
    public Task SyncHandler_GeneratesProducesMetadata() =>
        SyncHandlerSource.ShouldGenerate<ErrorOrInterceptorGenerator>(
            "ErrorOrInterceptors.g.cs",
            expectedContent: ".Produces<global::User>(200)",
            exactMatch: false);

    [Fact]
    public Task SyncHandler_GeneratesProducesProblem() =>
        SyncHandlerSource.ShouldGenerate<ErrorOrInterceptorGenerator>(
            "ErrorOrInterceptors.g.cs",
            expectedContent: ".ProducesProblem(404)",
            exactMatch: false);

    [Fact]
    public Task DeleteNoContent_GeneratesNoContentResponse() =>
        DeleteNoContentSource.ShouldGenerate<ErrorOrInterceptorGenerator>(
            "ErrorOrInterceptors.g.cs",
            expectedContent: "TypedResults.NoContent()",
            exactMatch: false);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Caching Tests
    // ═══════════════════════════════════════════════════════════════════════════════

    private static readonly string[] TrackingNames = ["Configuration", "CallSites", "GroupedCalls", "FinalOutput"];

    [Fact]
    public Task SyncHandler_CachesOnUnchangedSource() =>
        SyncHandlerSource.ShouldBeCached<ErrorOrInterceptorGenerator>(TrackingNames);

    [Fact]
    public Task MultipleEndpoints_CachesOnUnchangedSource() =>
        MultipleEndpointsSource.ShouldBeCached<ErrorOrInterceptorGenerator>(TrackingNames);

    // ═══════════════════════════════════════════════════════════════════════════════
    // No Diagnostics Tests
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public Task SyncHandler_NoDiagnostics() =>
        SyncHandlerSource.ShouldHaveNoDiagnostics<ErrorOrInterceptorGenerator>();

    [Fact]
    public Task AsyncHandler_NoDiagnostics() =>
        AsyncHandlerSource.ShouldHaveNoDiagnostics<ErrorOrInterceptorGenerator>();
}