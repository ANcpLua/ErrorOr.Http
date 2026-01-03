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
            app.MapMethods(@"/tags", new[] { "GET" }, (RequestDelegate)Invoke_Ep0)
            .WithMetadata(new global::Microsoft.AspNetCore.Routing.EndpointNameAttribute("global::Endpoints_GetTags"))
            .WithMetadata(new global::Microsoft.AspNetCore.Http.TagsAttribute("global::"))
            .WithMetadata(new global::Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute(typeof(string), 200))
            ;

        }

        public static IServiceCollection AddErrorOrEndpointJson<TContext>(this IServiceCollection services)
            where TContext : System.Text.Json.Serialization.JsonSerializerContext, new()
        {
            var context = new TContext();
            services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolverChain.Insert(0, context));
            return services;
        }

        private static async Task Invoke_Ep0(HttpContext ctx)
        {
            string? p0;
            if (!ctx.Request.Headers.TryGetValue("region", out var p0Raw) || p0Raw.Count == 0)
            {
                await TypedResults.BadRequest().ExecuteAsync(ctx); return;
            }
            else
            {
                p0 = p0Raw.ToString();
            }
            var p1Raw = ctx.Request.Query["ids"];
            var p1List = new global::System.Collections.Generic.List<int>();
            foreach (var item in p1Raw)
            {
                if (int.TryParse(item, out var parsedItem)) p1List.Add(parsedItem);
                else if (!string.IsNullOrEmpty(item)) { await TypedResults.BadRequest().ExecuteAsync(ctx); return; }
            }
            var p1 = p1List;
            var p2Raw = ctx.Request.Query["categories"];
            var p2List = new global::System.Collections.Generic.List<string>();
            foreach (var item in p2Raw)
            {
                if (!string.IsNullOrEmpty(item)) p2List.Add(item!);
            }
            var p2 = p2List;
            var result = global::Endpoints.GetTags(p0, p1!, p2!);
            var response = result.Match<global::Microsoft.AspNetCore.Http.IResult>(
                value => TypedResults.Ok(value),
                errors => ToProblem(errors));
            await response.ExecuteAsync(ctx);
        }

        private static bool TryGetRouteValue(HttpContext ctx, string name, out string? value)
        {
            if (!ctx.Request.RouteValues.TryGetValue(name, out var raw) || raw is null) { value = null; return false; }
            value = raw.ToString(); return value is not null;
        }

        private static bool TryGetQueryValue(HttpContext ctx, string name, out string? value)
        {
            if (!ctx.Request.Query.TryGetValue(name, out var raw) || raw.Count == 0) { value = null; return false; }
            value = raw.ToString(); return value is not null;
        }

        private static global::Microsoft.AspNetCore.Http.IResult ToProblem(System.Collections.Generic.IReadOnlyList<global::ErrorOr.Error> errors)
        {
            if (errors.Count == 0) return TypedResults.Problem();
            var hasValidation = false;
            for (var i = 0; i < errors.Count; i++) if (errors[i].Type == global::ErrorOr.ErrorType.Validation) { hasValidation = true; break; }
            if (hasValidation)
            {
                var dict = new global::System.Collections.Generic.Dictionary<string, string[]>();
                for (var i = 0; i < errors.Count; i++)
                {
                    var e = errors[i]; if (e.Type != global::ErrorOr.ErrorType.Validation) continue;
                    if (!dict.TryGetValue(e.Code, out var existing)) dict[e.Code] = new[] { e.Description };
                    else { var n = new string[existing.Length + 1]; existing.CopyTo(n, 0); n[existing.Length] = e.Description; dict[e.Code] = n; }
                }
                return TypedResults.ValidationProblem(dict);
            }
            var first = errors[0];
            var status = first.Type switch { global::ErrorOr.ErrorType.Validation => 400, global::ErrorOr.ErrorType.Unauthorized => 401, global::ErrorOr.ErrorType.Forbidden => 403, global::ErrorOr.ErrorType.NotFound => 404, global::ErrorOr.ErrorType.Conflict => 409, global::ErrorOr.ErrorType.Failure => 422, _ => 500 };
            return TypedResults.Problem(detail: first.Description, statusCode: status, title: first.Code);
        }
    }
}
