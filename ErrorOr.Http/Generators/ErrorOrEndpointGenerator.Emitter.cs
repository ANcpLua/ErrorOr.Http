using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ErrorOr.Http.Generators;

/// <summary>
///     Partial class containing all code emission logic for the endpoint generator.
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

    private static void EmitMappings(
        SourceProductionContext spc,
        ImmutableArray<EndpointDescriptor> endpoints,
        bool hasJsonTypes)
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

    private static void EmitJsonConfigExtension(StringBuilder code)
    {
        code.AppendLine("        /// <summary>");
        code.AppendLine("        /// Configures JSON serialization for ErrorOr endpoint DTOs.");
        code.AppendLine("        /// For NativeAOT, see the generated ErrorOrJsonContext.suggested.cs file.");
        code.AppendLine("        /// </summary>");
        code.AppendLine(
            "        public static IServiceCollection AddErrorOrEndpointJson<TContext>(this IServiceCollection services)");
        code.AppendLine("            where TContext : System.Text.Json.Serialization.JsonSerializerContext, new()");
        code.AppendLine("        {");
        code.AppendLine("            var context = new TContext();");
        code.AppendLine("            services.ConfigureHttpJsonOptions(options =>");
        code.AppendLine("            {");
        code.AppendLine("                options.SerializerOptions.TypeInfoResolverChain.Insert(0, context);");
        code.AppendLine("            });");
        code.AppendLine("            return services;");
        code.AppendLine("        }");
        code.AppendLine();
    }

    private static void EmitJsonContextSuggestion(SourceProductionContext spc, List<string> jsonTypes)
    {
        var code = new StringBuilder();
        code.AppendLine("// =============================================================================");
        code.AppendLine("// SUGGESTED JSON CONTEXT FOR NATIVE AOT");
        code.AppendLine("// =============================================================================");
        code.AppendLine("// To enable NativeAOT JSON serialization:");
        code.AppendLine("// 1. Create a new file ErrorOrJsonContext.cs in your project");
        code.AppendLine("// 2. Copy the code below (between #if ERROROR_JSON and #endif)");
        code.AppendLine("// 3. Add: builder.Services.AddErrorOrEndpointJson<ErrorOrJsonContext>();");
        code.AppendLine("// =============================================================================");
        code.AppendLine();
        code.AppendLine("#if ERROROR_JSON // Remove this line when copying to your project");
        code.AppendLine();
        code.AppendLine("#nullable enable");
        code.AppendLine("using System.Text.Json.Serialization;");
        code.AppendLine();
        code.AppendLine("[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]");

        foreach (var typeFqn in jsonTypes)
            code.AppendLine($"[JsonSerializable(typeof({typeFqn}))]");

        code.AppendLine("[JsonSerializable(typeof(global::Microsoft.AspNetCore.Mvc.ProblemDetails))]");
        code.AppendLine("[JsonSerializable(typeof(global::Microsoft.AspNetCore.Http.HttpValidationProblemDetails))]");

        code.AppendLine(
            "internal partial class ErrorOrJsonContext : System.Text.Json.Serialization.JsonSerializerContext");
        code.AppendLine("{");
        code.AppendLine("}");
        code.AppendLine();
        code.AppendLine("#endif // Remove this line when copying to your project");

        spc.AddSource("ErrorOrJsonContext.suggested.cs", SourceText.From(code.ToString(), Encoding.UTF8));
    }

    private static List<string> CollectJsonTypes(ImmutableArray<EndpointDescriptor> endpoints)
    {
        var types = new HashSet<string>(StringComparer.Ordinal);

        foreach (var ep in endpoints)
        {
            foreach (var param in ep.HandlerParameters.Items)
            {
                if (param.Source == EndpointParameterSource.Body)
                    types.Add(param.TypeFqn);
            }

            if (!IsNoContentType(ep.SuccessTypeFqn))
                types.Add(ep.SuccessTypeFqn);
        }

        var sorted = types.ToList();
        sorted.Sort(StringComparer.Ordinal);
        return sorted;
    }

    private static ImmutableArray<EndpointDescriptor> SortEndpoints(ImmutableArray<EndpointDescriptor> endpoints)
    {
        var list = new EndpointDescriptor[endpoints.Length];
        endpoints.CopyTo(list);
        Array.Sort(list, static (a, b) =>
        {
            var c = string.CompareOrdinal(a.HttpMethod, b.HttpMethod);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.Pattern, b.Pattern);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.HandlerContainingTypeFqn, b.HandlerContainingTypeFqn);
            return c != 0 ? c : string.CompareOrdinal(a.HandlerMethodName, b.HandlerMethodName);
        });
        return [..list];
    }

    private static void EmitMapCall(StringBuilder code, in EndpointDescriptor ep, int index)
    {
        var isNoContent = IsNoContentType(ep.SuccessTypeFqn);
        var successStatus = isNoContent ? 204 : 200;

        var methodName = $"Invoke_Ep{index}";
        var patternLiteral = "@\"" + ep.Pattern.Replace("\"", "\"\"") + "\"";
        var methodArray = "new[] { \"" + ep.HttpMethod.ToUpperInvariant() + "\" }";

        code.AppendLine($"            app.MapMethods({patternLiteral}, {methodArray}, (RequestDelegate){methodName})");

        // Generate OperationId and Tag from the handler type name
        var className = ep.HandlerContainingTypeFqn.Split('.').Last();
        if (className.Contains("::"))
            className = className.Substring(className.IndexOf("::") + 2);

        var tagName = className.EndsWith("Endpoints")
            ? className.Substring(0, className.Length - "Endpoints".Length)
            : className;

        var operationId = $"{className}_{ep.HandlerMethodName}";

        code.AppendLine(
            $"            .WithMetadata(new global::Microsoft.AspNetCore.Routing.EndpointNameAttribute(\"{operationId}\"))");
        code.AppendLine(
            $"            .WithMetadata(new global::Microsoft.AspNetCore.Http.TagsAttribute(\"{tagName}\"))");

        if (ep.IsObsolete)
        {
            var msg = ep.ObsoleteMessage is null ? "" : $"\"{ep.ObsoleteMessage}\"";
            var error = ep.IsObsoleteError ? ", true" : "";
            code.AppendLine($"            .WithMetadata(new global::System.ObsoleteAttribute({msg}{error}))");
        }

        code.AppendLine(
            isNoContent
                ? $"            .WithMetadata(new global::Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute({successStatus}))"
                : $"            .WithMetadata(new global::Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute(typeof({ep.SuccessTypeFqn}), {successStatus}))");

        if (!ep.InferredErrorTypes.IsDefaultOrEmpty)
        {
            foreach (var status in ep.InferredErrorTypes.Items.Select(MapErrorTypeToHttpStatus))
            {
                var problemType = status == 400
                    ? "global::Microsoft.AspNetCore.Http.HttpValidationProblemDetails"
                    : "global::Microsoft.AspNetCore.Mvc.ProblemDetails";
                code.AppendLine(
                    "            .WithMetadata(new global::Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute(");
                code.AppendLine($"                typeof({problemType}), {status}))");
            }
        }

        code.AppendLine("            ;");
        code.AppendLine();
    }

    private static void EmitInvoker(StringBuilder code, in EndpointDescriptor ep, int index)
    {
        code.AppendLine($"        private static async Task Invoke_Ep{index}(HttpContext ctx)");
        code.AppendLine("        {");

        var args = new StringBuilder();

        for (var i = 0; i < ep.HandlerParameters.Items.Length; i++)
        {
            var param = ep.HandlerParameters.Items[i];
            var paramName = $"p{i}";

            EmitParameterBinding(code, in param, paramName);

            if (i > 0) args.Append(", ");
            args.Append(BuildArgumentExpression(in param, paramName));
        }

        var awaitKeyword = ep.IsAsync ? "await " : "";
        code.AppendLine(
            $"            var result = {awaitKeyword}{ep.HandlerContainingTypeFqn}.{ep.HandlerMethodName}({args});");

        code.AppendLine("            var response = result.Match<global::Microsoft.AspNetCore.Http.IResult>(");
        var isPost = string.Equals(ep.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase);
        var successResult = IsNoContentType(ep.SuccessTypeFqn)
            ? "                _ => TypedResults.NoContent(),"
            : isPost
                ? "                value => TypedResults.Created(string.Empty, value),"
                : "                value => TypedResults.Ok(value),";
        code.AppendLine(successResult);
        code.AppendLine("                errors => ToProblem(errors));");
        code.AppendLine("            await response.ExecuteAsync(ctx);");
        code.AppendLine("        }");
        code.AppendLine();
    }

    private static void EmitAsParametersBinding(StringBuilder code, in EndpointParameter param, string paramName)
    {
        // Recursively bind children
        var childVars = new List<string>();
        for (var i = 0; i < param.Children.Items.Length; i++)
        {
            var child = param.Children.Items[i];
            var childVarName = $"{paramName}_c{i}";
            EmitParameterBinding(code, in child, childVarName);
            childVars.Add(BuildArgumentExpression(in child, childVarName));
        }

        // Construct the record/object
        // Assumption: The type has a constructor matching the order of Children
        code.AppendLine($"            var {paramName} = new {param.TypeFqn}({string.Join(", ", childVars)});");
    }

    private static void EmitBodyBinding(StringBuilder code, in EndpointParameter param, string paramName)
    {
        var nullableType = param.TypeFqn.EndsWith("?") ? param.TypeFqn : param.TypeFqn + "?";
        code.AppendLine($"            {nullableType} {paramName};");
        code.AppendLine("            try");
        code.AppendLine("            {");
        code.AppendLine(
            $"                {paramName} = await ctx.Request.ReadFromJsonAsync<{param.TypeFqn}>(cancellationToken: ctx.RequestAborted);");
        code.AppendLine("            }");
        code.AppendLine("            catch (global::System.Text.Json.JsonException)");
        code.AppendLine("            {");
        code.AppendLine("                await TypedResults.BadRequest().ExecuteAsync(ctx);");
        code.AppendLine("                return;");
        code.AppendLine("            }");
        code.AppendLine($"            if ({paramName} is null)");
        code.AppendLine("            {");
        code.AppendLine("                await TypedResults.BadRequest().ExecuteAsync(ctx);");
        code.AppendLine("                return;");
        code.AppendLine("            }");
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
            case EndpointParameterSource.AsParameters:
                EmitAsParametersBinding(code, in param, paramName);
                break;
            case EndpointParameterSource.Body:
                EmitBodyBinding(code, in param, paramName);
                break;
            case EndpointParameterSource.Service:
                code.AppendLine(
                    $"            var {paramName} = ctx.RequestServices.GetRequiredService<{param.TypeFqn}>();");
                break;
            case EndpointParameterSource.KeyedService:
                code.AppendLine(
                    $"            var {paramName} = ctx.RequestServices.GetRequiredKeyedService<{param.TypeFqn}>({param.KeyName});");
                break;
            case EndpointParameterSource.HttpContext:
                code.AppendLine($"            var {paramName} = ctx;");
                break;
            case EndpointParameterSource.CancellationToken:
                code.AppendLine($"            var {paramName} = ctx.RequestAborted;");
                break;
            default:
                throw new InvalidOperationException($"Unknown parameter source: {param.Source}");
        }
    }

    private static void EmitRouteBinding(StringBuilder code, in EndpointParameter param, string paramName)
    {
        var routeName = param.KeyName ?? param.Name;
        var rawName = paramName + "Raw";

        if (IsStringType(param.TypeFqn))
        {
            code.AppendLine($"            if (!TryGetRouteValue(ctx, \"{routeName}\", out var {paramName}))");
            code.AppendLine("            {");
            code.AppendLine("                await TypedResults.BadRequest().ExecuteAsync(ctx);");
            code.AppendLine("                return;");
            code.AppendLine("            }");
            return;
        }

        code.AppendLine(
            $"            if (!TryGetRouteValue(ctx, \"{routeName}\", out var {rawName}) || !{GetTryParseExpression(param.TypeFqn, rawName, paramName)})");
        code.AppendLine("            {");
        code.AppendLine("                await TypedResults.BadRequest().ExecuteAsync(ctx);");
        code.AppendLine("                return;");
        code.AppendLine("            }");
    }

    private static void EmitHeaderBinding(StringBuilder code, in EndpointParameter param, string paramName)
    {
        var key = param.KeyName ?? param.Name;
        var rawName = paramName + "Raw";
        var nullableType = param.TypeFqn.EndsWith("?") ? param.TypeFqn : param.TypeFqn + "?";
        code.AppendLine($"            {nullableType} {paramName};");

        code.AppendLine(
            $"            if (!ctx.Request.Headers.TryGetValue(\"{key}\", out var {rawName}) || {rawName}.Count == 0)");
        code.AppendLine("            {");
        if (param.IsNullable)
            code.AppendLine($"                {paramName} = default;");
        else
        {
            code.AppendLine("                await TypedResults.BadRequest().ExecuteAsync(ctx);");
            code.AppendLine("                return;");
        }

        code.AppendLine("            }");
        code.AppendLine("            else");
        code.AppendLine("            {");
        if (IsStringType(param.TypeFqn))
            code.AppendLine($"                {paramName} = {rawName}.ToString();");
        else
        {
            var tempName = paramName + "Temp";
            code.AppendLine(
                $"                if (!{GetTryParseExpression(param.TypeFqn, rawName + ".ToString()", tempName)})");
            code.AppendLine("                {");
            code.AppendLine("                    await TypedResults.BadRequest().ExecuteAsync(ctx);");
            code.AppendLine("                    return;");
            code.AppendLine("                }");
            code.AppendLine($"                {paramName} = {tempName};");
        }

        code.AppendLine("            }");
    }

    private static void EmitQueryBinding(StringBuilder code, in EndpointParameter param, string paramName)
    {
        var queryKey = param.KeyName ?? param.Name;
        if (param.IsCollection)
            EmitQueryCollectionBinding(code, in param, paramName, queryKey);
        else
            EmitQuerySingleBinding(code, in param, paramName, queryKey);
    }

    private static void EmitQueryCollectionBinding(StringBuilder code, in EndpointParameter param, string paramName,
        string queryKey)
    {
        var itemType = param.CollectionItemTypeFqn!;
        var rawListName = paramName + "RawList";
        var listType = $"global::System.Collections.Generic.List<{itemType}>";

        code.AppendLine($"            var {rawListName} = ctx.Request.Query[\"{queryKey}\"];");
        code.AppendLine($"            var {paramName}List = new {listType}();");
        code.AppendLine($"            foreach (var item in {rawListName})");
        code.AppendLine("            {");

        if (IsStringType(itemType))
            code.AppendLine($"                if (!string.IsNullOrEmpty(item)) {paramName}List.Add(item!);");
        else
        {
            // GetTryParseExpression returns e.g. "int.TryParse(item, out var parsedItem)"
            // We use "item" as input, and "parsedItem" as the out variable name.
            code.AppendLine($"                if ({GetTryParseExpression(itemType, "item", "parsedItem")})");
            code.AppendLine("                {");
            code.AppendLine($"                    {paramName}List.Add(parsedItem);");
            code.AppendLine("                }");
            code.AppendLine(
                "                else if (!string.IsNullOrEmpty(item))"); // Strict format check, ignore empty/null
            code.AppendLine("                {");
            code.AppendLine("                    await TypedResults.BadRequest().ExecuteAsync(ctx);");
            code.AppendLine("                    return;");
            code.AppendLine("                }");
        }

        code.AppendLine("            }");

        if (param.TypeFqn.Contains("[]") || param.TypeFqn.Contains("Array"))
            code.AppendLine($"            var {paramName} = {paramName}List.ToArray();");
        else
            code.AppendLine($"            var {paramName} = {paramName}List;");
    }

    private static void EmitQuerySingleBinding(StringBuilder code, in EndpointParameter param, string paramName,
        string queryKey)
    {
        var rawName = paramName + "Raw";
        var nullableType = param.TypeFqn.EndsWith("?") ? param.TypeFqn : param.TypeFqn + "?";
        code.AppendLine($"            {nullableType} {paramName};");

        if (IsStringType(param.TypeFqn))
        {
            code.AppendLine($"            if (!TryGetQueryValue(ctx, \"{queryKey}\", out var {rawName}))");
            code.AppendLine("            {");
            if (param.IsNullable)
                code.AppendLine($"                {paramName} = null;");
            else
            {
                code.AppendLine("                await TypedResults.BadRequest().ExecuteAsync(ctx);");
                code.AppendLine("                return;");
            }

            code.AppendLine("            }");
            code.AppendLine("            else");
            code.AppendLine("            {");
            code.AppendLine($"                {paramName} = {rawName};");
            code.AppendLine("            }");
            return;
        }

        // Primitives
        code.AppendLine($"            if (!TryGetQueryValue(ctx, \"{queryKey}\", out var {rawName}))");
        code.AppendLine("            {");
        if (param.IsNullable || !param.IsNonNullableValueType)
            code.AppendLine($"                {paramName} = default;");
        else
        {
            code.AppendLine("                await TypedResults.BadRequest().ExecuteAsync(ctx);");
            code.AppendLine("                return;");
        }

        code.AppendLine("            }");
        code.AppendLine("            else");
        code.AppendLine("            {");
        var tempName = paramName + "Temp";
        code.AppendLine($"                if (!{GetTryParseExpression(param.TypeFqn, rawName, tempName)})");
        code.AppendLine("                {");
        code.AppendLine("                    await TypedResults.BadRequest().ExecuteAsync(ctx);");
        code.AppendLine("                    return;");
        code.AppendLine("                }");
        code.AppendLine($"                {paramName} = {tempName};");
        code.AppendLine("            }");
    }

    private static string BuildArgumentExpression(in EndpointParameter param, string paramName)
    {
        return param.Source switch
        {
            EndpointParameterSource.Body when param.IsNonNullableValueType => paramName + ".Value",
            EndpointParameterSource.Body when !param.IsNullable => paramName + "!",
            EndpointParameterSource.Body => paramName,
            EndpointParameterSource.Route when param is { IsNullable: false, IsNonNullableValueType: false } =>
                paramName + "!",
            EndpointParameterSource.Query when param is { IsNullable: false, IsNonNullableValueType: false } =>
                paramName + "!",
            _ => paramName
        };
    }

    private static string GetTryParseExpression(string typeFqn, string rawName, string outputName)
    {
        var normalized = typeFqn;
        if (normalized.StartsWith("global::", StringComparison.Ordinal))
            normalized = normalized["global::".Length..];

        if (normalized.EndsWith("?", StringComparison.Ordinal))
            normalized = normalized[..^1];

        return normalized switch
        {
            "System.Int32" or "int" => $"int.TryParse({rawName}, out var {outputName})",
            "System.Int64" or "long" => $"long.TryParse({rawName}, out var {outputName})",
            "System.Int16" or "short" => $"short.TryParse({rawName}, out var {outputName})",
            "System.UInt32" or "uint" => $"uint.TryParse({rawName}, out var {outputName})",
            "System.UInt64" or "ulong" => $"ulong.TryParse({rawName}, out var {outputName})",
            "System.UInt16" or "ushort" => $"ushort.TryParse({rawName}, out var {outputName})",
            "System.Byte" or "byte" => $"byte.TryParse({rawName}, out var {outputName})",
            "System.SByte" or "sbyte" => $"sbyte.TryParse({rawName}, out var {outputName})",
            "System.Boolean" or "bool" => $"bool.TryParse({rawName}, out var {outputName})",
            "System.Decimal" or "decimal" => $"decimal.TryParse({rawName}, out var {outputName})",
            "System.Double" or "double" => $"double.TryParse({rawName}, out var {outputName})",
            "System.Single" or "float" => $"float.TryParse({rawName}, out var {outputName})",
            "System.Guid" => $"global::System.Guid.TryParse({rawName}, out var {outputName})",
            "System.DateTime" => $"global::System.DateTime.TryParse({rawName}, out var {outputName})",
            "System.DateTimeOffset" =>
                $"global::System.DateTimeOffset.TryParse({rawName}, out var {outputName})",
            "System.DateOnly" => $"global::System.DateOnly.TryParse({rawName}, out var {outputName})",
            "System.TimeOnly" => $"global::System.TimeOnly.TryParse({rawName}, out var {outputName})",
            "System.TimeSpan" => $"global::System.TimeSpan.TryParse({rawName}, out var {outputName})",
            _ => "false"
        };
    }

    private static bool IsStringType(string typeFqn)
    {
        return typeFqn is "global::System.String" or "System.String" or "string" or "global::string";
    }

    private static void EmitSupportMethods(StringBuilder code)
    {
        code.AppendLine(
            "        private static bool TryGetRouteValue(HttpContext ctx, string name, out string? value)");
        code.AppendLine("        {");
        code.AppendLine("            if (!ctx.Request.RouteValues.TryGetValue(name, out var raw) || raw is null)");
        code.AppendLine("            {");
        code.AppendLine("                value = null;");
        code.AppendLine("                return false;");
        code.AppendLine("            }");
        code.AppendLine();
        code.AppendLine("            value = raw.ToString();");
        code.AppendLine("            return value is not null;");
        code.AppendLine("        }");
        code.AppendLine();

        code.AppendLine(
            "        private static bool TryGetQueryValue(HttpContext ctx, string name, out string? value)");
        code.AppendLine("        {");
        code.AppendLine("            if (!ctx.Request.Query.TryGetValue(name, out var raw) || raw.Count == 0)");
        code.AppendLine("            {");
        code.AppendLine("                value = null;");
        code.AppendLine("                return false;");
        code.AppendLine("            }");
        code.AppendLine();
        code.AppendLine("            value = raw.ToString();");
        code.AppendLine("            return value is not null;");
        code.AppendLine("        }");
        code.AppendLine();

        code.AppendLine("        private static global::Microsoft.AspNetCore.Http.IResult ToProblem(");
        code.AppendLine("            System.Collections.Generic.IReadOnlyList<global::ErrorOr.Error> errors)");
        code.AppendLine("        {");
        code.AppendLine("            if (errors.Count == 0) return TypedResults.Problem();");
        code.AppendLine();
        code.AppendLine("            if (errors.Any(e => e.Type == global::ErrorOr.ErrorType.Validation))");
        code.AppendLine("            {");
        code.AppendLine("                var modelStateDictionary = errors");
        code.AppendLine("                    .Where(e => e.Type == global::ErrorOr.ErrorType.Validation)");
        code.AppendLine("                    .GroupBy(e => e.Code)");
        code.AppendLine("                    .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());");
        code.AppendLine("                return TypedResults.ValidationProblem(modelStateDictionary);");
        code.AppendLine("            }");
        code.AppendLine();
        code.AppendLine("            var first = errors[0];");
        code.AppendLine("            var status = first.Type switch");
        code.AppendLine("            {");
        code.AppendLine("                global::ErrorOr.ErrorType.Validation => 400,");
        code.AppendLine("                global::ErrorOr.ErrorType.Unauthorized => 401,");
        code.AppendLine("                global::ErrorOr.ErrorType.Forbidden => 403,");
        code.AppendLine("                global::ErrorOr.ErrorType.NotFound => 404,");
        code.AppendLine("                global::ErrorOr.ErrorType.Conflict => 409,");
        code.AppendLine("                global::ErrorOr.ErrorType.Failure => 422,");
        code.AppendLine("                _ => 500");
        code.AppendLine("            };");
        code.AppendLine("            return TypedResults.Problem(");
        code.AppendLine("                detail: first.Description,");
        code.AppendLine("                statusCode: status,");
        code.AppendLine("                title: first.Code);");
        code.AppendLine("        }");
    }

    private static bool IsNoContentType(string typeFqn)
    {
        return typeFqn.EndsWith("Deleted", StringComparison.Ordinal) ||
               typeFqn.EndsWith("Success", StringComparison.Ordinal);
    }

    private static int MapErrorTypeToHttpStatus(int errorType)
    {
        return errorType switch { 2 => 400, 3 => 409, 4 => 404, 5 => 401, 6 => 403, 0 => 422, _ => 500 };
    }
}
