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
            app.MapMethods(@"/upload", new[] { "POST" }, (RequestDelegate)Invoke_Ep0)
            .WithMetadata(new global::Microsoft.AspNetCore.Routing.EndpointNameAttribute("Handlers_Upload"))
            .WithMetadata(new global::Microsoft.AspNetCore.Http.TagsAttribute("Handlers"))
            .WithMetadata(new global::Microsoft.AspNetCore.Http.Metadata.AcceptsMetadata(new[] { "multipart/form-data" }))
            .WithMetadata(new global::Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute(204))
            ;

        }

        private static async Task Invoke_Ep0(HttpContext ctx)
        {
            if (!ctx.Request.HasFormContentType)
            {
                ctx.Response.StatusCode = 400;
                return;
            }

            var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);

            string p0;
            if (!form.TryGetValue("title", out var p0Raw) || p0Raw.Count == 0)
            {
                ctx.Response.StatusCode = 400;
                return;
            }
            else
            {
                p0 = p0Raw.ToString();
            }
            int p1;
            if (!form.TryGetValue("version", out var p1Raw) || p1Raw.Count == 0)
            {
                ctx.Response.StatusCode = 400;
                return;
            }
            else
            {
                if (!int.TryParse(p1Raw.ToString(), out var p1Temp))
                {
                    ctx.Response.StatusCode = 400;
                    return;
                }
                p1 = p1Temp;
            }
            var p2 = form.Files.GetFile("document");
            if (p2 is null)
            {
                ctx.Response.StatusCode = 400;
                return;
            }
            var result = global::Handlers.Upload(p0, p1, p2);
            var response = result.Match<global::Microsoft.AspNetCore.Http.IResult>(
                _ => TypedResults.NoContent(),
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
