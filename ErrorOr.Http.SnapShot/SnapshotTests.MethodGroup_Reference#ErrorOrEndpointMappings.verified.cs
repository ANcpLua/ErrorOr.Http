//HintName: ErrorOrEndpointMappings.cs
#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ErrorOr.Http.Generated
{
    public static class ErrorOrEndpointMappings
    {
        public static void MapErrorOrEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapMethods(@"/health", new[] { "GET" }, (RequestDelegate)Invoke_Ep0)
            .WithMetadata(new global::Microsoft.AspNetCore.Routing.EndpointNameAttribute("Endpoints_GetHealth"))
            .WithMetadata(new global::Microsoft.AspNetCore.Http.TagsAttribute(""))
            .WithMetadata(new global::Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute(typeof(string), 200))
            .WithMetadata(new global::Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute(
                typeof(global::Microsoft.AspNetCore.Mvc.ProblemDetails), 500))
            ;

        }

        /// <summary>
        /// Configures JSON serialization for ErrorOr endpoint DTOs.
        /// For NativeAOT, see the generated ErrorOrJsonContext.suggested.cs file.
        /// </summary>
        public static IServiceCollection AddErrorOrEndpointJson<TContext>(this IServiceCollection services)
            where TContext : System.Text.Json.Serialization.JsonSerializerContext, new()
        {
            var context = new TContext();
            services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, context);
            });
            return services;
        }

        private static async Task Invoke_Ep0(HttpContext ctx)
        {
            var result = global::Endpoints.GetHealth();
            var response = result.Match<global::Microsoft.AspNetCore.Http.IResult>(
                value => TypedResults.Ok(value),
                errors => ToProblem(errors));
            await response.ExecuteAsync(ctx);
        }

        private static bool TryGetRouteValue(HttpContext ctx, string name, out string? value)
        {
            if (!ctx.Request.RouteValues.TryGetValue(name, out var raw) || raw is null)
            {
                value = null;
                return false;
            }

            value = raw.ToString();
            return value is not null;
        }

        private static bool TryGetQueryValue(HttpContext ctx, string name, out string? value)
        {
            if (!ctx.Request.Query.TryGetValue(name, out var raw) || raw.Count == 0)
            {
                value = null;
                return false;
            }

            value = raw.ToString();
            return value is not null;
        }

        private static global::Microsoft.AspNetCore.Http.IResult ToProblem(
            System.Collections.Generic.IReadOnlyList<global::ErrorOr.Error> errors)
        {
            if (errors.Count == 0) return TypedResults.Problem();

            if (errors.Any(e => e.Type == global::ErrorOr.ErrorType.Validation))
            {
                var modelStateDictionary = errors
                    .Where(e => e.Type == global::ErrorOr.ErrorType.Validation)
                    .GroupBy(e => e.Code)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
                return TypedResults.ValidationProblem(modelStateDictionary);
            }

            var first = errors[0];
            var status = first.Type switch
            {
                global::ErrorOr.ErrorType.Validation => 400,
                global::ErrorOr.ErrorType.Unauthorized => 401,
                global::ErrorOr.ErrorType.Forbidden => 403,
                global::ErrorOr.ErrorType.NotFound => 404,
                global::ErrorOr.ErrorType.Conflict => 409,
                global::ErrorOr.ErrorType.Failure => 422,
                _ => 500
            };
            return TypedResults.Problem(
                detail: first.Description,
                statusCode: status,
                title: first.Code);
        }
    }
}
