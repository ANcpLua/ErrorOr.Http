using ANcpLua.Roslyn.Utilities.Testing;
using ErrorOr.Http.Generators;
using Xunit;

namespace ErrorOr.Http.Tests;

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


    private static readonly string[] s_trackingNames = ["EndpointCollection"];


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
    public Task AmbiguousParameter_ReportsError()
    {
        return AmbiguousParameterSource.ShouldHaveDiagnostics<ErrorOrEndpointGenerator>(
            GeneratorTestExtensions.Diagnostic("EOE004"),
            GeneratorTestExtensions.Diagnostic("EOE004"));
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


    private const string StreamParameterSource = """
                                                 using ErrorOr;
                                                 using ErrorOr.Http;
                                                 using System.IO;
                                                 using System.Threading.Tasks;

                                                 public static class Endpoints
                                                 {
                                                     [ErrorOrEndpoint("POST", "/upload/raw")]
                                                     public static async Task<ErrorOr<string>> UploadRaw(Stream body)
                                                     {
                                                         using var ms = new MemoryStream();
                                                         await body.CopyToAsync(ms);
                                                         return $"Received {ms.Length} bytes";
                                                     }
                                                 }
                                                 """;

    private const string PipeReaderParameterSource = """
                                                     using ErrorOr;
                                                     using ErrorOr.Http;
                                                     using System.IO.Pipelines;
                                                     using System.Threading.Tasks;

                                                     public static class Endpoints
                                                     {
                                                         [ErrorOrEndpoint("POST", "/upload/pipe")]
                                                         public static async Task<ErrorOr<string>> UploadPipe(PipeReader reader)
                                                         {
                                                             var result = await reader.ReadAsync();
                                                             reader.AdvanceTo(result.Buffer.End);
                                                             return $"Received {result.Buffer.Length} bytes";
                                                         }
                                                     }
                                                     """;

    private const string StreamWithBodyConflictSource = """
                                                        using ErrorOr;
                                                        using ErrorOr.Http;
                                                        using System.IO;
                                                        using Microsoft.AspNetCore.Mvc;

                                                        public static class Endpoints
                                                        {
                                                            [ErrorOrEndpoint("POST", "/conflict")]
                                                            public static ErrorOr<string> Conflict(
                                                                Stream body,
                                                                [FromBody] string dto) => "conflict";
                                                        }
                                                        """;

    private const string PipeReaderWithFormConflictSource = """
                                                            using ErrorOr;
                                                            using ErrorOr.Http;
                                                            using System.IO.Pipelines;
                                                            using Microsoft.AspNetCore.Http;

                                                            public static class Endpoints
                                                            {
                                                                [ErrorOrEndpoint("POST", "/conflict")]
                                                                public static ErrorOr<string> Conflict(
                                                                    PipeReader reader,
                                                                    IFormFile file) => "conflict";
                                                            }
                                                            """;

    [Fact]
    public Task StreamParameter_Compiles()
    {
        return StreamParameterSource.ShouldCompile<ErrorOrEndpointGenerator>();
    }

    [Fact]
    public Task StreamParameter_GeneratesCorrectBinding()
    {
        return StreamParameterSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            "var p0 = ctx.Request.Body;",
            false);
    }

    [Fact]
    public Task PipeReaderParameter_Compiles()
    {
        return PipeReaderParameterSource.ShouldCompile<ErrorOrEndpointGenerator>();
    }

    [Fact]
    public Task PipeReaderParameter_GeneratesCorrectBinding()
    {
        return PipeReaderParameterSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            "var p0 = ctx.Request.BodyReader;",
            false);
    }

    [Fact]
    public Task StreamWithBodyConflict_ReportsError()
    {
        return StreamWithBodyConflictSource.ShouldHaveDiagnostics<ErrorOrEndpointGenerator>(
            GeneratorTestExtensions.Diagnostic("EOE006"));
    }

    [Fact]
    public Task PipeReaderWithFormConflict_ReportsError()
    {
        return PipeReaderWithFormConflictSource.ShouldHaveDiagnostics<ErrorOrEndpointGenerator>(
            GeneratorTestExtensions.Diagnostic("EOE006"));
    }



    private const string SseSimpleSource = """
                                           using ErrorOr;
                                           using ErrorOr.Http;
                                           using System.Collections.Generic;

                                           public static class Endpoints
                                           {
                                               [ErrorOrEndpoint("GET", "/stream")]
                                               public static ErrorOr<IAsyncEnumerable<int>> StreamNumbers()
                                               {
                                                   return StreamAsync();

                                                   static async IAsyncEnumerable<int> StreamAsync()
                                                   {
                                                       for (var i = 0; i < 10; i++)
                                                       {
                                                           yield return i;
                                                       }
                                                   }
                                               }
                                           }
                                           """;

    private const string SseSseItemSource = """
                                            using ErrorOr;
                                            using ErrorOr.Http;
                                            using System.Collections.Generic;
                                            using System.Net.ServerSentEvents;

                                            public static class Endpoints
                                            {
                                                [ErrorOrEndpoint("GET", "/events")]
                                                public static ErrorOr<IAsyncEnumerable<SseItem<string>>> StreamEvents()
                                                {
                                                    return StreamAsync();

                                                    static async IAsyncEnumerable<SseItem<string>> StreamAsync()
                                                    {
                                                        yield return new SseItem<string>("Hello");
                                                        yield return new SseItem<string>("World");
                                                    }
                                                }
                                            }
                                            """;

    private const string SseAsyncSource = """
                                          using ErrorOr;
                                          using ErrorOr.Http;
                                          using System.Collections.Generic;
                                          using System.Threading.Tasks;

                                          public static class Endpoints
                                          {
                                              [ErrorOrEndpoint("GET", "/async-stream")]
                                              public static async Task<ErrorOr<IAsyncEnumerable<string>>> AsyncStream()
                                              {
                                                  await Task.Delay(1);
                                                  return StreamAsync();

                                                  static async IAsyncEnumerable<string> StreamAsync()
                                                  {
                                                      yield return "data";
                                                  }
                                              }
                                          }
                                          """;

    private const string SseWithErrorSource = """
                                              using ErrorOr;
                                              using ErrorOr.Http;
                                              using System.Collections.Generic;

                                              public static class Endpoints
                                              {
                                                  [ErrorOrEndpoint("GET", "/feed/{id}")]
                                                  public static ErrorOr<IAsyncEnumerable<string>> GetFeed(int id)
                                                  {
                                                      if (id < 0)
                                                          return Error.Validation("Feed.InvalidId", "Invalid ID");
                                                      if (id == 0)
                                                          return Error.NotFound("Feed.NotFound", "Feed not found");

                                                      return StreamAsync();

                                                      static async IAsyncEnumerable<string> StreamAsync()
                                                      {
                                                          yield return "item";
                                                      }
                                                  }
                                              }
                                              """;

    [Fact]
    public Task SseSimple_Compiles()
    {
        return SseSimpleSource.ShouldCompile<ErrorOrEndpointGenerator>();
    }

    [Fact]
    public Task SseSimple_GeneratesServerSentEvents()
    {
        return SseSimpleSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            "TypedResults.ServerSentEvents(result.Value).ExecuteAsync(ctx)",
            false);
    }

    [Fact]
    public Task SseSimple_GeneratesTextEventStreamContentType()
    {
        return SseSimpleSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            ".Produces(200, contentType: \"text/event-stream\")",
            false);
    }

    [Fact]
    public Task SseSseItem_Compiles()
    {
        return SseSseItemSource.ShouldCompile<ErrorOrEndpointGenerator>();
    }

    [Fact]
    public Task SseSseItem_GeneratesServerSentEvents()
    {
        return SseSseItemSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            "TypedResults.ServerSentEvents(result.Value).ExecuteAsync(ctx)",
            false);
    }

    [Fact]
    public Task SseAsync_Compiles()
    {
        return SseAsyncSource.ShouldCompile<ErrorOrEndpointGenerator>();
    }

    [Fact]
    public Task SseAsync_GeneratesAwait()
    {
        return SseAsyncSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            "var result = await global::Endpoints.AsyncStream(",
            false);
    }

    [Fact]
    public Task SseWithError_Compiles()
    {
        return SseWithErrorSource.ShouldCompile<ErrorOrEndpointGenerator>();
    }

    [Fact]
    public Task SseWithError_GeneratesErrorHandling()
    {
        return SseWithErrorSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            "if (result.IsError)",
            false);
    }

    [Fact]
    public Task SseWithError_GeneratesToProblem()
    {
        return SseWithErrorSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            "await ToProblem(result.Errors).ExecuteAsync(ctx);",
            false);
    }

    [Fact]
    public Task SseWithError_GeneratesInferredErrors()
    {
        return SseWithErrorSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            "typeof(global::Microsoft.AspNetCore.Http.HttpValidationProblemDetails), 400",
            false);
    }

    [Fact]
    public Task SseWithError_GeneratesNotFoundError()
    {
        return SseWithErrorSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            "typeof(global::Microsoft.AspNetCore.Mvc.ProblemDetails), 404",
            false);
    }



    private const string TryParseParameterSource = """
                                                   using ErrorOr;
                                                   using ErrorOr.Http;

                                                   public static class Endpoints
                                                   {
                                                       [ErrorOrEndpoint("GET", "/map")]
                                                       public static ErrorOr<string> GetMap(Point point)
                                                           => $"Point: {point.X}, {point.Y}";
                                                   }

                                                   public class Point
                                                   {
                                                       public double X { get; set; }
                                                       public double Y { get; set; }

                                                       public static bool TryParse(string? value, out Point? point)
                                                       {
                                                           var segments = value?.Split(',');
                                                           if (segments?.Length == 2
                                                               && double.TryParse(segments[0], out var x)
                                                               && double.TryParse(segments[1], out var y))
                                                           {
                                                               point = new Point { X = x, Y = y };
                                                               return true;
                                                           }
                                                           point = null;
                                                           return false;
                                                       }
                                                   }
                                                   """;

    private const string TryParseRouteSource = """
                                               using ErrorOr;
                                               using ErrorOr.Http;

                                               public static class Endpoints
                                               {
                                                   [ErrorOrEndpoint("GET", "/location/{coords}")]
                                                   public static ErrorOr<string> GetLocation(Point coords)
                                                       => $"Location: {coords.X}, {coords.Y}";
                                               }

                                               public class Point
                                               {
                                                   public double X { get; set; }
                                                   public double Y { get; set; }

                                                   public static bool TryParse(string? value, out Point? point)
                                                   {
                                                       var segments = value?.Split(',');
                                                       if (segments?.Length == 2
                                                           && double.TryParse(segments[0], out var x)
                                                           && double.TryParse(segments[1], out var y))
                                                       {
                                                           point = new Point { X = x, Y = y };
                                                           return true;
                                                       }
                                                       point = null;
                                                       return false;
                                                   }
                                               }
                                               """;

    private const string BindAsyncParameterSource = """
                                                    using ErrorOr;
                                                    using ErrorOr.Http;
                                                    using Microsoft.AspNetCore.Http;
                                                    using System.Threading.Tasks;

                                                    public static class Endpoints
                                                    {
                                                        [ErrorOrEndpoint("GET", "/products")]
                                                        public static ErrorOr<string> GetProducts(PagingData paging)
                                                            => $"Page: {paging.Page}, SortBy: {paging.SortBy}";
                                                    }

                                                    public class PagingData
                                                    {
                                                        public string? SortBy { get; init; }
                                                        public int Page { get; init; } = 1;

                                                        public static ValueTask<PagingData?> BindAsync(HttpContext context)
                                                        {
                                                            int.TryParse(context.Request.Query["page"], out var page);

                                                            return ValueTask.FromResult<PagingData?>(new PagingData
                                                            {
                                                                SortBy = context.Request.Query["sortBy"],
                                                                Page = page == 0 ? 1 : page
                                                            });
                                                        }
                                                    }
                                                    """;

    [Fact]
    public Task TryParseParameter_Compiles()
    {
        return TryParseParameterSource.ShouldCompile<ErrorOrEndpointGenerator>();
    }

    [Fact]
    public Task TryParseParameter_GeneratesTryParseCall()
    {
        return TryParseParameterSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            "Point.TryParse(",
            false);
    }

    [Fact]
    public Task TryParseParameter_GeneratesQueryBinding()
    {
        return TryParseParameterSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            "TryGetQueryValue(ctx, \"point\"",
            false);
    }

    [Fact]
    public Task TryParseRoute_Compiles()
    {
        return TryParseRouteSource.ShouldCompile<ErrorOrEndpointGenerator>();
    }

    [Fact]
    public Task TryParseRoute_GeneratesRouteBinding()
    {
        return TryParseRouteSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            "TryGetRouteValue(ctx, \"coords\"",
            false);
    }

    [Fact]
    public Task BindAsyncParameter_Compiles()
    {
        return BindAsyncParameterSource.ShouldCompile<ErrorOrEndpointGenerator>();
    }

    [Fact]
    public Task BindAsyncParameter_GeneratesBindAsyncCall()
    {
        return BindAsyncParameterSource.ShouldGenerate<ErrorOrEndpointGenerator>(
            "ErrorOrEndpointMappings.cs",
            "await global::PagingData.BindAsync(ctx)",
            false);
    }

}
