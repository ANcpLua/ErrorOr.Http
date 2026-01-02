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
            app.MapMethods(@"/a/{id}", new[] { "GET" }, (RequestDelegate)Invoke_Ep0)
            .WithMetadata(new global::Microsoft.AspNetCore.Routing.EndpointNameAttribute("Endpoints_GetUser"))
            .WithMetadata(new global::Microsoft.AspNetCore.Http.TagsAttribute(""))
            .WithMetadata(new global::Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute(typeof(global::User), 200))
            .WithMetadata(new global::Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute(
                typeof(global::Microsoft.AspNetCore.Mvc.ProblemDetails), 404))
            ;

            app.MapMethods(@"/b/{id}", new[] { "GET" }, (RequestDelegate)Invoke_Ep1)
            .WithMetadata(new global::Microsoft.AspNetCore.Routing.EndpointNameAttribute("Endpoints_GetUser"))
            .WithMetadata(new global::Microsoft.AspNetCore.Http.TagsAttribute(""))
            .WithMetadata(new global::Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute(typeof(global::User), 200))
            .WithMetadata(new global::Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute(
                typeof(global::Microsoft.AspNetCore.Mvc.ProblemDetails), 404))
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
            if (!TryGetRouteValue(ctx, "id", out var p0Raw) || !int.TryParse(p0Raw, out var p0))
            {
                await TypedResults.BadRequest().ExecuteAsync(ctx);
                return;
            }
            var result = global::Endpoints.GetUser(p0);
            var response = result.Match<global::Microsoft.AspNetCore.Http.IResult>(
                value => TypedResults.Ok(value),
                errors => ToProblem(errors));
            await response.ExecuteAsync(ctx);
        }

        private static async Task Invoke_Ep1(HttpContext ctx)
        {
            if (!TryGetRouteValue(ctx, "id", out var p0Raw) || !int.TryParse(p0Raw, out var p0))
            {
                await TypedResults.BadRequest().ExecuteAsync(ctx);
                return;
            }
            var result = global::Endpoints.GetUser(p0);
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

            var hasValidation = false;
            for (var i = 0; i < errors.Count; i++)
            {
                if (errors[i].Type == global::ErrorOr.ErrorType.Validation)
                {
                    hasValidation = true;
                    break;
                }
            }

            if (hasValidation)
            {
                // Build dictionary without LINQ allocation
                var modelStateDictionary = new global::System.Collections.Generic.Dictionary<string, string[]>();
                for (var i = 0; i < errors.Count; i++)
                {
                    var error = errors[i];
                    if (error.Type != global::ErrorOr.ErrorType.Validation) continue;

                    if (!modelStateDictionary.TryGetValue(error.Code, out var existing))
                    {
                        modelStateDictionary[error.Code] = new[] { error.Description };
                    }
                    else
                    {
                        var newArray = new string[existing.Length + 1];
                        existing.CopyTo(newArray, 0);
                        newArray[existing.Length] = error.Description;
                        modelStateDictionary[error.Code] = newArray;
                    }
                }
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
