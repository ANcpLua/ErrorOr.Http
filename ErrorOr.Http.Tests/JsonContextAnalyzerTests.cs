using ANcpLua.Roslyn.Utilities.Testing;
using ErrorOr.Http.Generators;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ErrorOr.Http.Tests;

/// <summary>
///     Tests for EOE022: JsonSerializerContext coverage analyzer.
///     Validates that types used in endpoints are registered in a JsonSerializerContext.
/// </summary>
[Collection("NonParallelRoslyn")]
public class JsonContextAnalyzerTests
{
    private const string Usings = """
                                  using ErrorOr;
                                  using ErrorOr.Http;
                                  using System.Text.Json.Serialization;
                                  """;

    /// <summary>
    ///     When no JsonSerializerContext exists, analyzer skips (MSBuild targets handle warning).
    /// </summary>
    [Fact]
    public Task NoJsonContext_SkipsAnalysis()
    {
        return """
               using ErrorOr;
               using ErrorOr.Http;

               public static class Endpoints
               {
                   [Get( "/test")]
                   public static ErrorOr<string> Get() => "ok";
               }
               """.ShouldHaveNoDiagnostics<ErrorOrEndpointGenerator>();
    }

    /// <summary>
    ///     When JsonSerializerContext exists but is missing types, EOE022 should fire.
    /// </summary>
    [Fact]
    public Task IncompleteJsonContext_ReportsWarningForMissingTypes()
    {
        return $$"""
                 {{Usings}}

                 public static class Endpoints
                 {
                     [Get( "/users")]
                     public static ErrorOr<User> GetUser() => new User("Test");
                 }

                 public record User(string Name);

                 [JsonSerializable(typeof(string))]
                 internal partial class AppJsonContext : JsonSerializerContext { }
                 """.ShouldHaveDiagnostics<ErrorOrEndpointGenerator>(
            GeneratorTestExtensions.Diagnostic("EOE022", DiagnosticSeverity.Warning),
            GeneratorTestExtensions.Diagnostic("EOE022", DiagnosticSeverity.Warning),
            GeneratorTestExtensions.Diagnostic("EOE022", DiagnosticSeverity.Warning));
    }

    /// <summary>
    ///     When JsonSerializerContext has all required types, no EOE022 should fire.
    /// </summary>
    [Fact]
    public Task CompleteJsonContext_NoDiagnostics()
    {
        return $$"""
                 {{Usings}}
                 using Microsoft.AspNetCore.Mvc;
                 using Microsoft.AspNetCore.Http;

                 public static class Endpoints
                 {
                     [Get( "/users")]
                     public static ErrorOr<User> GetUser() => new User("Test");
                 }

                 public record User(string Name);

                 [JsonSerializable(typeof(User))]
                 [JsonSerializable(typeof(ProblemDetails))]
                 [JsonSerializable(typeof(HttpValidationProblemDetails))]
                 internal partial class AppJsonContext : JsonSerializerContext { }
                 """.ShouldHaveNoDiagnostics<ErrorOrEndpointGenerator>();
    }

    /// <summary>
    ///     Body parameters should also be checked for JsonSerializerContext coverage.
    /// </summary>
    [Fact]
    public Task BodyParameter_IncludedInCheck()
    {
        return $$"""
                 {{Usings}}
                 using Microsoft.AspNetCore.Mvc;
                 using Microsoft.AspNetCore.Http;

                 public static class Endpoints
                 {
                     [Post( "/users")]
                     public static ErrorOr<Created> CreateUser([FromBody] CreateUserRequest request)
                         => Result.Created;
                 }

                 public record CreateUserRequest(string Name, string Email);

                 [JsonSerializable(typeof(ProblemDetails))]
                 [JsonSerializable(typeof(HttpValidationProblemDetails))]
                 internal partial class AppJsonContext : JsonSerializerContext { }
                 """.ShouldHaveDiagnostics<ErrorOrEndpointGenerator>(
            GeneratorTestExtensions.Diagnostic("EOE022", DiagnosticSeverity.Warning));
    }

    /// <summary>
    ///     Result types (Deleted, Created, etc.) should not require serialization.
    /// </summary>
    [Fact]
    public Task ResultTypes_NotRequiredInContext()
    {
        return $$"""
                 {{Usings}}
                 using Microsoft.AspNetCore.Mvc;
                 using Microsoft.AspNetCore.Http;

                 public static class Endpoints
                 {
                     [Delete( "/users/{id}")]
                     public static ErrorOr<Deleted> Delete(int id) => Result.Deleted;
                 }

                 [JsonSerializable(typeof(ProblemDetails))]
                 [JsonSerializable(typeof(HttpValidationProblemDetails))]
                 internal partial class AppJsonContext : JsonSerializerContext { }
                 """.ShouldHaveNoDiagnostics<ErrorOrEndpointGenerator>();
    }

    /// <summary>
    ///     Multiple endpoints should aggregate all needed types.
    /// </summary>
    [Fact]
    public Task MultipleEndpoints_AllTypesChecked()
    {
        return $$"""
                 {{Usings}}
                 using Microsoft.AspNetCore.Mvc;
                 using Microsoft.AspNetCore.Http;

                 public static class Endpoints
                 {
                     [Get( "/users")]
                     public static ErrorOr<User[]> GetUsers() => new[] { new User("Test") };

                     [Get( "/products")]
                     public static ErrorOr<Product> GetProduct() => new Product("Widget");
                 }

                 public record User(string Name);
                 public record Product(string Name);

                 [JsonSerializable(typeof(User[]))]
                 [JsonSerializable(typeof(ProblemDetails))]
                 [JsonSerializable(typeof(HttpValidationProblemDetails))]
                 internal partial class AppJsonContext : JsonSerializerContext { }
                 """.ShouldHaveDiagnostics<ErrorOrEndpointGenerator>(
            GeneratorTestExtensions.Diagnostic("EOE022", DiagnosticSeverity.Warning));
    }
}
