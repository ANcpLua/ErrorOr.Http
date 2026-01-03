using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ErrorOr.Http.Generators;

/// <summary>
///     Code emission logic for endpoint mappings.
/// </summary>
public sealed partial class ErrorOrEndpointGenerator
{
    internal static void EmitEndpoints(SourceProductionContext spc, ImmutableArray<EndpointDescriptor> endpoints)
    {
        var sorted = SortEndpoints(endpoints);
        var jsonTypes = CollectJsonTypes(sorted);

        EmitMappings(spc, sorted, jsonTypes.Count > 0);

        if (jsonTypes.Count > 0)
            EmitJsonContextSuggestion(spc, jsonTypes);
    }

    private static void EmitMappings(SourceProductionContext spc, ImmutableArray<EndpointDescriptor> endpoints, bool hasJsonTypes)
    {
        var code = new StringBuilder();
        code.AppendLine("#nullable enable");
        code.AppendLine("using System;");
        code.AppendLine("using System.Linq;");
        code.AppendLine("using System.Threading.Tasks;");
        code.AppendLine("using Microsoft.AspNetCore.Builder;");
        code.AppendLine("using Microsoft.AspNetCore.Http;");
        code.AppendLine("using Microsoft.AspNetCore.Routing;");
        code.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        code.AppendLine();
        code.AppendLine("namespace ErrorOr.Http.Generated");
        code.AppendLine("{");
        code.AppendLine("    public static class ErrorOrEndpointMappings");
        code.AppendLine("    {");
        code.AppendLine("        public static void MapErrorOrEndpoints(this IEndpointRouteBuilder app)");
        code.AppendLine("        {");

        for (var i = 0; i < endpoints.Length; i++)
            EmitMapCall(code, endpoints[i], i);

        code.AppendLine("        }");
        code.AppendLine();

        if (hasJsonTypes)
            EmitJsonConfigExtension(code);

        for (var i = 0; i < endpoints.Length; i++)
            EmitInvoker(code, endpoints[i], i);

        EmitSupportMethods(code);
        code.AppendLine("    }");
        code.AppendLine("}");

        spc.AddSource("ErrorOrEndpointMappings.cs", SourceText.From(code.ToString(), Encoding.UTF8));
    }

    private static void EmitMapCall(StringBuilder code, in EndpointDescriptor ep, int index)
    {
        var isNoContent = IsNoContentType(ep.SuccessTypeFqn);
        var successStatus = isNoContent ? 204 : (ep.HttpMethod == "POST" ? 201 : 200);

        code.AppendLine($"            app.MapMethods(@\"{ep.Pattern}\", new[] {{ \"{ep.HttpMethod}\" }}, (RequestDelegate)Invoke_Ep{index})");

        var className = ExtractTypeName(ep.HandlerContainingTypeFqn);
        var tagName = className.EndsWith("Endpoints") ? className[..^"Endpoints".Length] : className;

        code.AppendLine($"            .WithMetadata(new global::Microsoft.AspNetCore.Routing.EndpointNameAttribute(\"{className}_{ep.HandlerMethodName}\"))");
        code.AppendLine($"            .WithMetadata(new global::Microsoft.AspNetCore.Http.TagsAttribute(\"{tagName}\"))");

        if (HasFormParams(ep))
            code.AppendLine("            .WithMetadata(new global::Microsoft.AspNetCore.Http.Metadata.AcceptsMetadata(new[] { \"multipart/form-data\" }))");

        if (ep.IsSse)
            code.AppendLine("            .Produces(200, contentType: \"text/event-stream\")");
        else if (isNoContent)
            code.AppendLine($"            .WithMetadata(new global::Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute({successStatus}))");
        else
            code.AppendLine($"            .WithMetadata(new global::Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute(typeof({ep.SuccessTypeFqn}), {successStatus}))");

        foreach (var errorType in ep.InferredErrorTypes.Items)
        {
            var status = MapErrorTypeToHttpStatus(errorType);
            var problemType = status == 400 ? WellKnownTypes.Fqn.HttpValidationProblemDetails : WellKnownTypes.Fqn.ProblemDetails;
            code.AppendLine($"            .WithMetadata(new global::Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute(typeof({problemType}), {status}))");
        }

        code.AppendLine("            ;");
        code.AppendLine();
    }

    private static void EmitInvoker(StringBuilder code, in EndpointDescriptor ep, int index)
    {
        code.AppendLine($"        private static async Task Invoke_Ep{index}(HttpContext ctx)");
        code.AppendLine("        {");

        if (HasFormParams(ep))
            EmitFormContentTypeGuard(code);

        var args = new StringBuilder();
        for (var i = 0; i < ep.HandlerParameters.Items.Length; i++)
        {
            var param = ep.HandlerParameters.Items[i];
            EmitParameterBinding(code, in param, $"p{i}");
            if (i > 0) args.Append(", ");
            args.Append(BuildArgumentExpression(in param, $"p{i}"));
        }

        var awaitKeyword = ep.IsAsync ? "await " : "";
        code.AppendLine($"            var result = {awaitKeyword}{ep.HandlerContainingTypeFqn}.{ep.HandlerMethodName}({args});");

        if (ep.IsSse)
        {
            code.AppendLine("            if (result.IsError) { await ToProblem(result.Errors).ExecuteAsync(ctx); return; }");
            code.AppendLine("            await TypedResults.ServerSentEvents(result.Value).ExecuteAsync(ctx);");
        }
        else
        {
            var successResult = IsNoContentType(ep.SuccessTypeFqn)
                ? "_ => TypedResults.NoContent()"
                : ep.HttpMethod == "POST"
                    ? "value => TypedResults.Created(string.Empty, value)"
                    : "value => TypedResults.Ok(value)";

            code.AppendLine("            var response = result.Match<global::Microsoft.AspNetCore.Http.IResult>(");
            code.AppendLine($"                {successResult},");
            code.AppendLine("                errors => ToProblem(errors));");
            code.AppendLine("            await response.ExecuteAsync(ctx);");
        }

        code.AppendLine("        }");
        code.AppendLine();
    }

    private static void EmitParameterBinding(StringBuilder code, in EndpointParameter param, string paramName)
    {
        switch (param.Source)
        {
            case EndpointParameterSource.Route:
                EmitRouteBinding(code, in param, paramName);
                break;
            case EndpointParameterSource.Query:
                EmitQueryBinding(code, in param, paramName);
                break;
            case EndpointParameterSource.Header:
                EmitHeaderBinding(code, in param, paramName);
                break;
            case EndpointParameterSource.Body:
                EmitBodyBinding(code, in param, paramName);
                break;
            case EndpointParameterSource.Service:
                code.AppendLine($"            var {paramName} = ctx.RequestServices.GetRequiredService<{param.TypeFqn}>();");
                break;
            case EndpointParameterSource.KeyedService:
                code.AppendLine($"            var {paramName} = ctx.RequestServices.GetRequiredKeyedService<{param.TypeFqn}>({param.KeyName});");
                break;
            case EndpointParameterSource.HttpContext:
                code.AppendLine($"            var {paramName} = ctx;");
                break;
            case EndpointParameterSource.CancellationToken:
                code.AppendLine($"            var {paramName} = ctx.RequestAborted;");
                break;
            case EndpointParameterSource.Stream:
                code.AppendLine($"            var {paramName} = ctx.Request.Body;");
                break;
            case EndpointParameterSource.PipeReader:
                code.AppendLine($"            var {paramName} = ctx.Request.BodyReader;");
                break;
            case EndpointParameterSource.FormFile:
                code.AppendLine($"            var {paramName} = form.Files.GetFile(\"{param.KeyName ?? param.Name}\");");
                if (!param.IsNullable)
                    code.AppendLine($"            if ({paramName} is null) {{ ctx.Response.StatusCode = 400; return; }}");
                break;
            case EndpointParameterSource.FormFiles:
                code.AppendLine($"            var {paramName} = form.Files;");
                break;
            case EndpointParameterSource.FormCollection:
                code.AppendLine($"            var {paramName} = form;");
                break;
            case EndpointParameterSource.Form:
                EmitFormBinding(code, in param, paramName);
                break;
            case EndpointParameterSource.AsParameters:
                EmitAsParametersBinding(code, in param, paramName);
                break;
        }
    }

    private static void EmitRouteBinding(StringBuilder code, in EndpointParameter param, string paramName)
    {
        var routeName = param.KeyName ?? param.Name;
        if (IsStringType(param.TypeFqn))
        {
            code.AppendLine($"            if (!TryGetRouteValue(ctx, \"{routeName}\", out var {paramName})) {{ await TypedResults.BadRequest().ExecuteAsync(ctx); return; }}");
        }
        else
        {
            code.AppendLine($"            if (!TryGetRouteValue(ctx, \"{routeName}\", out var {paramName}Raw) || !{GetTryParseExpression(param.TypeFqn, paramName + "Raw", paramName, param.CustomBinding)}) {{ await TypedResults.BadRequest().ExecuteAsync(ctx); return; }}");
        }
    }

    private static void EmitQueryBinding(StringBuilder code, in EndpointParameter param, string paramName)
    {
        // Handle BindAsync custom binding
        if (param.CustomBinding is CustomBindingMethod.BindAsync or CustomBindingMethod.BindAsyncWithParam)
        {
            var baseType = param.TypeFqn.TrimEnd('?');
            code.AppendLine($"            var {paramName} = await {baseType}.BindAsync(ctx);");
            if (!param.IsNullable)
                code.AppendLine($"            if ({paramName} is null) {{ await TypedResults.BadRequest().ExecuteAsync(ctx); return; }}");
            return;
        }

        var queryKey = param.KeyName ?? param.Name;
        if (param.IsCollection)
        {
            var itemType = param.CollectionItemTypeFqn!;
            code.AppendLine($"            var {paramName}Raw = ctx.Request.Query[\"{queryKey}\"];");
            code.AppendLine($"            var {paramName}List = new global::System.Collections.Generic.List<{itemType}>();");
            code.AppendLine($"            foreach (var item in {paramName}Raw)");
            code.AppendLine("            {");
            if (IsStringType(itemType))
                code.AppendLine($"                if (!string.IsNullOrEmpty(item)) {paramName}List.Add(item!);");
            else
            {
                code.AppendLine($"                if ({GetTryParseExpression(itemType, "item", "parsedItem")}) {paramName}List.Add(parsedItem);");
                code.AppendLine("                else if (!string.IsNullOrEmpty(item)) { await TypedResults.BadRequest().ExecuteAsync(ctx); return; }");
            }
            code.AppendLine("            }");
            code.AppendLine($"            var {paramName} = {paramName}List;");
        }
        else
        {
            code.AppendLine($"            {param.TypeFqn}? {paramName};");
            code.AppendLine($"            if (!TryGetQueryValue(ctx, \"{queryKey}\", out var {paramName}Raw))");
            code.AppendLine("            {");
            code.AppendLine(param.IsNullable ? $"                {paramName} = default;" : "                await TypedResults.BadRequest().ExecuteAsync(ctx); return;");
            code.AppendLine("            }");
            code.AppendLine("            else");
            code.AppendLine("            {");
            if (IsStringType(param.TypeFqn))
                code.AppendLine($"                {paramName} = {paramName}Raw;");
            else
            {
                code.AppendLine($"                if (!{GetTryParseExpression(param.TypeFqn, paramName + "Raw", paramName + "Temp", param.CustomBinding)}) {{ await TypedResults.BadRequest().ExecuteAsync(ctx); return; }}");
                code.AppendLine($"                {paramName} = {paramName}Temp;");
            }
            code.AppendLine("            }");
        }
    }

    private static void EmitHeaderBinding(StringBuilder code, in EndpointParameter param, string paramName)
    {
        var key = param.KeyName ?? param.Name;
        code.AppendLine($"            {param.TypeFqn}? {paramName};");
        code.AppendLine($"            if (!ctx.Request.Headers.TryGetValue(\"{key}\", out var {paramName}Raw) || {paramName}Raw.Count == 0)");
        code.AppendLine("            {");
        code.AppendLine(param.IsNullable ? $"                {paramName} = default;" : "                await TypedResults.BadRequest().ExecuteAsync(ctx); return;");
        code.AppendLine("            }");
        code.AppendLine("            else");
        code.AppendLine("            {");
        code.AppendLine(IsStringType(param.TypeFqn)
            ? $"                {paramName} = {paramName}Raw.ToString();"
            : $"                if (!{GetTryParseExpression(param.TypeFqn, paramName + "Raw.ToString()", paramName + "Temp")}) {{ await TypedResults.BadRequest().ExecuteAsync(ctx); return; }} {paramName} = {paramName}Temp;");
        code.AppendLine("            }");
    }

    private static void EmitBodyBinding(StringBuilder code, in EndpointParameter param, string paramName)
    {
        code.AppendLine($"            {param.TypeFqn}? {paramName};");
        code.AppendLine("            try");
        code.AppendLine("            {");
        code.AppendLine($"                {paramName} = await ctx.Request.ReadFromJsonAsync<{param.TypeFqn}>(cancellationToken: ctx.RequestAborted);");
        code.AppendLine("            }");
        code.AppendLine("            catch (global::System.Text.Json.JsonException)");
        code.AppendLine("            {");
        code.AppendLine("                await TypedResults.BadRequest().ExecuteAsync(ctx); return;");
        code.AppendLine("            }");
        code.AppendLine($"            if ({paramName} is null) {{ await TypedResults.BadRequest().ExecuteAsync(ctx); return; }}");
    }

    private static void EmitFormBinding(StringBuilder code, in EndpointParameter param, string paramName)
    {
        if (!param.Children.IsDefaultOrEmpty)
        {
            for (var i = 0; i < param.Children.Items.Length; i++)
            {
                var child = param.Children.Items[i];
                EmitParameterBinding(code, in child, $"{paramName}_f{i}");
            }
            var args = string.Join(", ", param.Children.Items.Select((_, i) => $"{paramName}_f{i}"));
            code.AppendLine($"            var {paramName} = new {param.TypeFqn}({args});");
        }
        else
        {
            var fieldName = param.KeyName ?? param.Name;
            code.AppendLine($"            {param.TypeFqn} {paramName};");
            code.AppendLine($"            if (!form.TryGetValue(\"{fieldName}\", out var {paramName}Raw) || {paramName}Raw.Count == 0)");
            code.AppendLine("            {");
            code.AppendLine(param.IsNullable ? $"                {paramName} = default;" : "                ctx.Response.StatusCode = 400; return;");
            code.AppendLine("            }");
            code.AppendLine("            else");
            code.AppendLine("            {");
            if (IsStringType(param.TypeFqn))
                code.AppendLine($"                {paramName} = {paramName}Raw.ToString();");
            else
            {
                code.AppendLine($"                if (!{GetTryParseExpression(param.TypeFqn, paramName + "Raw.ToString()", paramName + "Temp")}) {{ ctx.Response.StatusCode = 400; return; }}");
                code.AppendLine($"                {paramName} = {paramName}Temp;");
            }
            code.AppendLine("            }");
        }
    }

    private static void EmitAsParametersBinding(StringBuilder code, in EndpointParameter param, string paramName)
    {
        var childVars = new List<string>();
        for (var i = 0; i < param.Children.Items.Length; i++)
        {
            var child = param.Children.Items[i];
            var childVarName = $"{paramName}_c{i}";
            EmitParameterBinding(code, in child, childVarName);
            childVars.Add(BuildArgumentExpression(in child, childVarName));
        }
        code.AppendLine($"            var {paramName} = new {param.TypeFqn}({string.Join(", ", childVars)});");
    }

    private static void EmitFormContentTypeGuard(StringBuilder code)
    {
        code.AppendLine("            if (!ctx.Request.HasFormContentType) { ctx.Response.StatusCode = 400; return; }");
        code.AppendLine("            var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);");
        code.AppendLine();
    }

    private static void EmitJsonConfigExtension(StringBuilder code)
    {
        code.AppendLine("        public static IServiceCollection AddErrorOrEndpointJson<TContext>(this IServiceCollection services)");
        code.AppendLine("            where TContext : System.Text.Json.Serialization.JsonSerializerContext, new()");
        code.AppendLine("        {");
        code.AppendLine("            var context = new TContext();");
        code.AppendLine("            services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolverChain.Insert(0, context));");
        code.AppendLine("            return services;");
        code.AppendLine("        }");
        code.AppendLine();
    }

    private static void EmitJsonContextSuggestion(SourceProductionContext spc, List<string> jsonTypes)
    {
        var code = new StringBuilder();
        code.AppendLine("// SUGGESTED JSON CONTEXT FOR NATIVE AOT");
        code.AppendLine("#if ERROROR_JSON");
        code.AppendLine("[System.Text.Json.Serialization.JsonSourceGenerationOptions(DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]");
        foreach (var type in jsonTypes)
            code.AppendLine($"[System.Text.Json.Serialization.JsonSerializable(typeof({type}))]");
        code.AppendLine("[System.Text.Json.Serialization.JsonSerializable(typeof(global::Microsoft.AspNetCore.Mvc.ProblemDetails))]");
        code.AppendLine("[System.Text.Json.Serialization.JsonSerializable(typeof(global::Microsoft.AspNetCore.Http.HttpValidationProblemDetails))]");
        code.AppendLine("internal partial class ErrorOrJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }");
        code.AppendLine("#endif");
        spc.AddSource("ErrorOrJsonContext.suggested.cs", SourceText.From(code.ToString(), Encoding.UTF8));
    }

    private static void EmitSupportMethods(StringBuilder code)
    {
        code.AppendLine("        private static bool TryGetRouteValue(HttpContext ctx, string name, out string? value)");
        code.AppendLine("        {");
        code.AppendLine("            if (!ctx.Request.RouteValues.TryGetValue(name, out var raw) || raw is null) { value = null; return false; }");
        code.AppendLine("            value = raw.ToString(); return value is not null;");
        code.AppendLine("        }");
        code.AppendLine();
        code.AppendLine("        private static bool TryGetQueryValue(HttpContext ctx, string name, out string? value)");
        code.AppendLine("        {");
        code.AppendLine("            if (!ctx.Request.Query.TryGetValue(name, out var raw) || raw.Count == 0) { value = null; return false; }");
        code.AppendLine("            value = raw.ToString(); return value is not null;");
        code.AppendLine("        }");
        code.AppendLine();
        code.AppendLine("        private static global::Microsoft.AspNetCore.Http.IResult ToProblem(System.Collections.Generic.IReadOnlyList<global::ErrorOr.Error> errors)");
        code.AppendLine("        {");
        code.AppendLine("            if (errors.Count == 0) return TypedResults.Problem();");
        code.AppendLine("            var hasValidation = false;");
        code.AppendLine("            for (var i = 0; i < errors.Count; i++) if (errors[i].Type == global::ErrorOr.ErrorType.Validation) { hasValidation = true; break; }");
        code.AppendLine("            if (hasValidation)");
        code.AppendLine("            {");
        code.AppendLine("                var dict = new global::System.Collections.Generic.Dictionary<string, string[]>();");
        code.AppendLine("                for (var i = 0; i < errors.Count; i++)");
        code.AppendLine("                {");
        code.AppendLine("                    var e = errors[i]; if (e.Type != global::ErrorOr.ErrorType.Validation) continue;");
        code.AppendLine("                    if (!dict.TryGetValue(e.Code, out var existing)) dict[e.Code] = new[] { e.Description };");
        code.AppendLine("                    else { var n = new string[existing.Length + 1]; existing.CopyTo(n, 0); n[existing.Length] = e.Description; dict[e.Code] = n; }");
        code.AppendLine("                }");
        code.AppendLine("                return TypedResults.ValidationProblem(dict);");
        code.AppendLine("            }");
        code.AppendLine("            var first = errors[0];");
        code.AppendLine("            var status = first.Type switch { global::ErrorOr.ErrorType.Validation => 400, global::ErrorOr.ErrorType.Unauthorized => 401, global::ErrorOr.ErrorType.Forbidden => 403, global::ErrorOr.ErrorType.NotFound => 404, global::ErrorOr.ErrorType.Conflict => 409, global::ErrorOr.ErrorType.Failure => 422, _ => 500 };");
        code.AppendLine("            return TypedResults.Problem(detail: first.Description, statusCode: status, title: first.Code);");
        code.AppendLine("        }");
    }

    private static string BuildArgumentExpression(in EndpointParameter param, string paramName)
    {
        return param.Source switch
        {
            EndpointParameterSource.Body when !param.IsNullable => paramName + "!",
            EndpointParameterSource.Route when !param.IsNullable && !param.IsNonNullableValueType => paramName + "!",
            EndpointParameterSource.Query when !param.IsNullable && !param.IsNonNullableValueType => paramName + "!",
            _ => paramName
        };
    }

    private static string GetTryParseExpression(string typeFqn, string rawName, string outputName, CustomBindingMethod customBinding = CustomBindingMethod.None)
    {
        // Handle custom TryParse types first
        if (customBinding is CustomBindingMethod.TryParse or CustomBindingMethod.TryParseWithFormat)
        {
            var baseType = typeFqn.TrimEnd('?');
            return $"{baseType}.TryParse({rawName}, out var {outputName})";
        }

        var normalized = typeFqn.Replace("global::", "").TrimEnd('?');
        return normalized switch
        {
            "System.Int32" or "int" => $"int.TryParse({rawName}, out var {outputName})",
            "System.Int64" or "long" => $"long.TryParse({rawName}, out var {outputName})",
            "System.Boolean" or "bool" => $"bool.TryParse({rawName}, out var {outputName})",
            "System.Guid" => $"global::System.Guid.TryParse({rawName}, out var {outputName})",
            "System.DateTime" => $"global::System.DateTime.TryParse({rawName}, out var {outputName})",
            "System.DateOnly" => $"global::System.DateOnly.TryParse({rawName}, out var {outputName})",
            "System.TimeOnly" => $"global::System.TimeOnly.TryParse({rawName}, out var {outputName})",
            "System.Double" or "double" => $"double.TryParse({rawName}, out var {outputName})",
            "System.Decimal" or "decimal" => $"decimal.TryParse({rawName}, out var {outputName})",
            _ => "false"
        };
    }

    private static bool IsStringType(string typeFqn) => typeFqn is "global::System.String" or "System.String" or "string";
    private static bool HasFormParams(in EndpointDescriptor ep) => ep.HandlerParameters.Items.Any(p => p.Source is EndpointParameterSource.Form or EndpointParameterSource.FormFile or EndpointParameterSource.FormFiles or EndpointParameterSource.FormCollection);
    private static int MapErrorTypeToHttpStatus(int errorType) => errorType switch { 2 => 400, 3 => 409, 4 => 404, 5 => 401, 6 => 403, 0 => 422, _ => 500 };
    private static string ExtractTypeName(string fqn) { var i = fqn.LastIndexOf('.'); var n = i >= 0 ? fqn[(i + 1)..] : fqn; return n.StartsWith("::") ? n[2..] : n; }

    private static ImmutableArray<EndpointDescriptor> SortEndpoints(ImmutableArray<EndpointDescriptor> endpoints)
    {
        var list = new EndpointDescriptor[endpoints.Length];
        endpoints.CopyTo(list);
        Array.Sort(list, static (a, b) =>
        {
            var c = string.CompareOrdinal(a.HttpMethod, b.HttpMethod);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.Pattern, b.Pattern);
            return c != 0 ? c : string.CompareOrdinal(a.HandlerMethodName, b.HandlerMethodName);
        });
        return [.. list];
    }

    private static List<string> CollectJsonTypes(ImmutableArray<EndpointDescriptor> endpoints)
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ep in endpoints)
        {
            foreach (var p in ep.HandlerParameters.Items)
                if (p.Source == EndpointParameterSource.Body)
                    types.Add(p.TypeFqn);

            if (ep.IsSse && ep.SseItemTypeFqn is not null)
                types.Add(ep.SseItemTypeFqn);
            else if (!IsNoContentType(ep.SuccessTypeFqn))
                types.Add(ep.SuccessTypeFqn);
        }
        var sorted = types.ToList();
        sorted.Sort(StringComparer.Ordinal);
        return sorted;
    }
}
