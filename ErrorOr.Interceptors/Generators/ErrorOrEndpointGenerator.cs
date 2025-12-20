using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using ErrorOr.Interceptors.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ErrorOr.Interceptors.Generators;

/// <summary>
/// Incremental generator that produces typed ErrorOr endpoint mappings.
/// NO REFLECTION - generates compile-time typed lambdas for full AOT compatibility.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ErrorOrEndpointGenerator : IIncrementalGenerator
{
    private const string EnableGeneratorProperty = "build_property.EnableErrorOrEndpointGenerator";

    private static class TrackingNames
    {
        public const string IsEnabled = nameof(IsEnabled);
        public const string AllEndpoints = nameof(AllEndpoints);
        public const string FilteredEndpoints = nameof(FilteredEndpoints);
    }

    private static readonly string[] RouteAttributeNames =
    [
        "ErrorOr.Interceptors.ErrorOrGetAttribute",
        "ErrorOr.Interceptors.ErrorOrPostAttribute",
        "ErrorOr.Interceptors.ErrorOrPutAttribute",
        "ErrorOr.Interceptors.ErrorOrDeleteAttribute",
        "ErrorOr.Interceptors.ErrorOrPatchAttribute"
    ];

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Emit attributes first - runs BEFORE other generator logic
        context.RegisterPostInitializationOutput(EmitAttributes);

        // Check MSBuild property
        var isEnabled = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
            {
                if (options.GlobalOptions.TryGetValue(EnableGeneratorProperty, out var value))
                    return !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
                return true;
            })
            .WithTrackingName(TrackingNames.IsEnabled);

        // Emit ErrorOrHttp helper when enabled
        context.RegisterSourceOutput(isEnabled, static (spc, enabled) =>
        {
            if (!enabled) return;
            EmitErrorOrHttp(spc);
        });

        // Discover methods with route attributes
        var getEndpoints = CreateEndpointProvider(context, RouteAttributeNames[0]);
        var postEndpoints = CreateEndpointProvider(context, RouteAttributeNames[1]);
        var putEndpoints = CreateEndpointProvider(context, RouteAttributeNames[2]);
        var deleteEndpoints = CreateEndpointProvider(context, RouteAttributeNames[3]);
        var patchEndpoints = CreateEndpointProvider(context, RouteAttributeNames[4]);

        // Combine all endpoints
        var allEndpoints = getEndpoints
            .Collect()
            .Combine(postEndpoints.Collect())
            .Combine(putEndpoints.Collect())
            .Combine(deleteEndpoints.Collect())
            .Combine(patchEndpoints.Collect())
            .Select(static (tuple, _) =>
            {
                var builder = ImmutableArray.CreateBuilder<EndpointInfo>();
                builder.AddRange(tuple.Left.Left.Left.Left);
                builder.AddRange(tuple.Left.Left.Left.Right);
                builder.AddRange(tuple.Left.Left.Right);
                builder.AddRange(tuple.Left.Right);
                builder.AddRange(tuple.Right);
                return new EquatableArray<EndpointInfo>(builder.ToImmutable());
            })
            .WithTrackingName(TrackingNames.AllEndpoints);

        // Combine with enabled flag and generate
        var enabledEndpoints = allEndpoints
            .Combine(isEnabled)
            .Select(static (pair, _) => pair.Right ? pair.Left : EquatableArray<EndpointInfo>.Empty)
            .WithTrackingName(TrackingNames.FilteredEndpoints);

        context.RegisterSourceOutput(enabledEndpoints, static (spc, endpoints) =>
        {
            if (endpoints.IsDefaultOrEmpty) return;
            EmitEndpointMappings(spc, endpoints);
        });
    }

    private static IncrementalValuesProvider<EndpointInfo> CreateEndpointProvider(
        IncrementalGeneratorInitializationContext context,
        string attributeName)
    {
        return context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeName,
                static (node, _) => node is MethodDeclarationSyntax,
                TransformMethod)
            .Where(static r => r.Endpoint.HasValue)
            .Select(static (r, _) => r.Endpoint!.Value);
    }

    private static ExtractResult TransformMethod(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        var method = (IMethodSymbol)ctx.TargetSymbol;
        var containingType = method.ContainingType;

        // Must be static
        if (!method.IsStatic)
            return new ExtractResult();

        var attr = ctx.Attributes.FirstOrDefault();
        if (attr is null)
            return new ExtractResult();

        // Extract route from constructor arg
        var route = attr.ConstructorArguments.FirstOrDefault().Value?.ToString() ?? "";

        // Extract HTTP method from attribute name
        var httpMethod = attr.AttributeClass?.Name switch
        {
            "ErrorOrGetAttribute" => "Get",
            "ErrorOrPostAttribute" => "Post",
            "ErrorOrPutAttribute" => "Put",
            "ErrorOrDeleteAttribute" => "Delete",
            "ErrorOrPatchAttribute" => "Patch",
            _ => "Get"
        };

        // Extract success status
        var successStatus = GetNamedArgValue(attr, "SuccessStatus", httpMethod switch
        {
            "Post" => 201,
            "Delete" => 204,
            _ => 200
        });

        var endpointName = GetNamedArgValue<string?>(attr, "Name", null);
        var summary = GetNamedArgValue<string?>(attr, "Summary", null);

        // Extract TValue from return type
        var (valueType, isAsync, isValid) = ExtractValueType(method.ReturnType);
        if (!isValid || valueType is null)
            return new ExtractResult();

        // Get parameters
        var parameters = method.Parameters
            .Select(static p => new ParameterInfo(
                p.Name,
                p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                GetParameterAttributes(p)))
            .ToImmutableArray();

        // Get class info
        var classNamespace = containingType.ContainingNamespace.IsGlobalNamespace
            ? ""
            : containingType.ContainingNamespace.ToDisplayString();

        var classPrefix = "";
        var classTag = containingType.Name.Replace("Endpoints", "");

        // Check for [ErrorOrEndpoints] on class
        foreach (var classAttr in containingType.GetAttributes())
        {
            if (classAttr.AttributeClass?.Name == "ErrorOrEndpointsAttribute")
            {
                classPrefix = GetNamedArgValue(classAttr, "Prefix", "");
                classTag = GetNamedArgValue<string?>(classAttr, "Tag", null) ?? classTag;
            }
        }

        return new ExtractResult(Endpoint: new EndpointInfo(
            Namespace: classNamespace,
            ClassName: containingType.Name,
            ClassFqn: containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ClassPrefix: classPrefix,
            ClassTag: classTag,
            MethodName: method.Name,
            Route: route,
            HttpMethod: httpMethod,
            SuccessStatus: successStatus,
            ValueType: valueType,
            IsAsync: isAsync,
            Parameters: new EquatableArray<ParameterInfo>(parameters),
            EndpointName: endpointName,
            Summary: summary));
    }

    private static (string? valueType, bool isAsync, bool isValid) ExtractValueType(ITypeSymbol returnType)
    {
        if (returnType is not INamedTypeSymbol { IsGenericType: true } namedType)
            return (null, false, false);

        var name = namedType.ConstructedFrom.ToDisplayString();

        // Task<ErrorOr<T>> or ValueTask<ErrorOr<T>>
        if (name is "System.Threading.Tasks.Task<TResult>" or "System.Threading.Tasks.ValueTask<TResult>")
        {
            var innerType = namedType.TypeArguments[0];
            if (innerType is INamedTypeSymbol { IsGenericType: true } errorOrType &&
                errorOrType.ConstructedFrom.ToDisplayString() == "ErrorOr.ErrorOr<TValue>")
            {
                var valueType = errorOrType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return (valueType, true, true);
            }
        }

        // ErrorOr<T>
        if (name == "ErrorOr.ErrorOr<TValue>")
        {
            var valueType = namedType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return (valueType, false, true);
        }

        return (null, false, false);
    }

    private static string GetParameterAttributes(IParameterSymbol param)
    {
        var sb = new StringBuilder();
        foreach (var attr in param.GetAttributes())
        {
            var mapped = attr.AttributeClass?.Name switch
            {
                "FromBodyAttribute" => "[global::Microsoft.AspNetCore.Mvc.FromBody]",
                "FromQueryAttribute" => "[global::Microsoft.AspNetCore.Mvc.FromQuery]",
                "FromRouteAttribute" => "[global::Microsoft.AspNetCore.Mvc.FromRoute]",
                "FromHeaderAttribute" => "[global::Microsoft.AspNetCore.Mvc.FromHeader]",
                "FromServicesAttribute" => "[global::Microsoft.AspNetCore.Mvc.FromServices]",
                "AsParametersAttribute" => "[global::Microsoft.AspNetCore.Http.AsParameters]",
                _ => null
            };

            if (mapped is not null)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(mapped);
            }
        }

        return sb.ToString();
    }

    private static T GetNamedArgValue<T>(AttributeData attr, string name, T defaultValue)
    {
        foreach (var arg in attr.NamedArguments)
            if (arg.Key == name && arg.Value.Value is T value)
                return value;

        return defaultValue;
    }

    private static void EmitAttributes(IncrementalGeneratorPostInitializationContext ctx)
    {
        const string source = """
            // <auto-generated/>
            #nullable enable
            namespace ErrorOr.Interceptors;

            /// <summary>
            /// Marks a class as containing ErrorOr endpoint methods.
            /// </summary>
            [global::System.AttributeUsage(global::System.AttributeTargets.Class)]
            internal sealed class ErrorOrEndpointsAttribute : global::System.Attribute
            {
                /// <summary>Route prefix for all endpoints in this class.</summary>
                public string Prefix { get; set; } = "";
                /// <summary>OpenAPI tag for all endpoints in this class.</summary>
                public string? Tag { get; set; }
            }

            /// <summary>Base class for ErrorOr route attributes.</summary>
            [global::System.AttributeUsage(global::System.AttributeTargets.Method)]
            internal abstract class ErrorOrRouteAttribute : global::System.Attribute
            {
                /// <summary>The route pattern.</summary>
                public string Route { get; }
                /// <summary>HTTP status code for successful responses.</summary>
                public int SuccessStatus { get; set; }
                /// <summary>OpenAPI operation name.</summary>
                public string? Name { get; set; }
                /// <summary>OpenAPI summary.</summary>
                public string? Summary { get; set; }

                protected ErrorOrRouteAttribute(string route, int defaultStatus)
                {
                    Route = route;
                    SuccessStatus = defaultStatus;
                }
            }

            /// <summary>Maps a GET endpoint returning ErrorOr&lt;T&gt;.</summary>
            [global::System.AttributeUsage(global::System.AttributeTargets.Method)]
            internal sealed class ErrorOrGetAttribute(string route = "") : ErrorOrRouteAttribute(route, 200);

            /// <summary>Maps a POST endpoint returning ErrorOr&lt;T&gt;.</summary>
            [global::System.AttributeUsage(global::System.AttributeTargets.Method)]
            internal sealed class ErrorOrPostAttribute(string route = "") : ErrorOrRouteAttribute(route, 201);

            /// <summary>Maps a PUT endpoint returning ErrorOr&lt;T&gt;.</summary>
            [global::System.AttributeUsage(global::System.AttributeTargets.Method)]
            internal sealed class ErrorOrPutAttribute(string route = "") : ErrorOrRouteAttribute(route, 200);

            /// <summary>Maps a DELETE endpoint returning ErrorOr&lt;T&gt;.</summary>
            [global::System.AttributeUsage(global::System.AttributeTargets.Method)]
            internal sealed class ErrorOrDeleteAttribute(string route = "") : ErrorOrRouteAttribute(route, 204);

            /// <summary>Maps a PATCH endpoint returning ErrorOr&lt;T&gt;.</summary>
            [global::System.AttributeUsage(global::System.AttributeTargets.Method)]
            internal sealed class ErrorOrPatchAttribute(string route = "") : ErrorOrRouteAttribute(route, 200);
            """;

        ctx.AddSource("ErrorOrAttributes.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static void EmitErrorOrHttp(SourceProductionContext spc)
    {
        const string source = """
            // <auto-generated/>
            // ErrorOrHttp - Typed mapping helper. NO REFLECTION.
            #nullable enable

            namespace ErrorOr.Interceptors.Generated;

            /// <summary>
            /// Maps ErrorOr&lt;TValue&gt; to IResult. NO REFLECTION - TValue is known at compile time.
            /// </summary>
            internal static class ErrorOrHttp
            {
                /// <summary>Maps ErrorOr&lt;TValue&gt; to IResult with the specified success status.</summary>
                public static global::Microsoft.AspNetCore.Http.IResult Map<TValue>(
                    global::ErrorOr.ErrorOr<TValue> result,
                    int successStatus = 200)
                {
                    if (!result.IsError)
                        return MapSuccess(result.Value, successStatus);

                    return MapError(result.Errors);
                }

                /// <summary>Maps async ErrorOr&lt;TValue&gt; to IResult.</summary>
                public static async global::System.Threading.Tasks.Task<global::Microsoft.AspNetCore.Http.IResult> MapAsync<TValue>(
                    global::System.Threading.Tasks.Task<global::ErrorOr.ErrorOr<TValue>> task,
                    int successStatus = 200)
                    => Map(await task.ConfigureAwait(false), successStatus);

                /// <summary>Maps async ErrorOr&lt;TValue&gt; to IResult.</summary>
                public static async global::System.Threading.Tasks.ValueTask<global::Microsoft.AspNetCore.Http.IResult> MapAsync<TValue>(
                    global::System.Threading.Tasks.ValueTask<global::ErrorOr.ErrorOr<TValue>> task,
                    int successStatus = 200)
                    => Map(await task.ConfigureAwait(false), successStatus);

                private static global::Microsoft.AspNetCore.Http.IResult MapSuccess<TValue>(TValue value, int successStatus)
                {
                    // Handle ErrorOr marker types
                    if (value is global::ErrorOr.Deleted or global::ErrorOr.Updated or global::ErrorOr.Success or global::ErrorOr.Created)
                        return global::Microsoft.AspNetCore.Http.TypedResults.NoContent();

                    return successStatus switch
                    {
                        204 => global::Microsoft.AspNetCore.Http.TypedResults.NoContent(),
                        201 => global::Microsoft.AspNetCore.Http.TypedResults.Created((string?)null, value),
                        202 => global::Microsoft.AspNetCore.Http.TypedResults.Accepted((string?)null, value),
                        _ => global::Microsoft.AspNetCore.Http.TypedResults.Ok(value)
                    };
                }

                private static global::Microsoft.AspNetCore.Http.IResult MapError(
                    global::System.Collections.Generic.IReadOnlyList<global::ErrorOr.Error> errors)
                {
                    var primary = errors.Count > 0 ? errors[0] : global::ErrorOr.Error.Unexpected();

                    return primary.Type switch
                    {
                        global::ErrorOr.ErrorType.Validation => global::Microsoft.AspNetCore.Http.TypedResults.ValidationProblem(
                            global::System.Linq.Enumerable.ToDictionary(
                                global::System.Linq.Enumerable.GroupBy(
                                    global::System.Linq.Enumerable.Where(errors, static e => e.Type == global::ErrorOr.ErrorType.Validation),
                                    static e => e.Code),
                                static g => g.Key,
                                static g => global::System.Linq.Enumerable.ToArray(
                                    global::System.Linq.Enumerable.Select(g, static e => e.Description)))),

                        global::ErrorOr.ErrorType.NotFound => global::Microsoft.AspNetCore.Http.TypedResults.NotFound(),
                        global::ErrorOr.ErrorType.Unauthorized => global::Microsoft.AspNetCore.Http.TypedResults.Unauthorized(),
                        global::ErrorOr.ErrorType.Forbidden => global::Microsoft.AspNetCore.Http.TypedResults.Forbid(),

                        _ => global::Microsoft.AspNetCore.Http.TypedResults.Problem(
                            title: primary.Code,
                            detail: primary.Description,
                            statusCode: MapErrorToStatus(primary))
                    };
                }

                private static int MapErrorToStatus(global::ErrorOr.Error error) => error.Type switch
                {
                    global::ErrorOr.ErrorType.Validation => 400,
                    global::ErrorOr.ErrorType.Unauthorized => 401,
                    global::ErrorOr.ErrorType.Forbidden => 403,
                    global::ErrorOr.ErrorType.NotFound => 404,
                    global::ErrorOr.ErrorType.Conflict => 409,
                    global::ErrorOr.ErrorType.Failure => 422,
                    _ => 500
                };
            }
            """;

        spc.AddSource("ErrorOrHttp.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static void EmitEndpointMappings(SourceProductionContext spc, EquatableArray<EndpointInfo> endpoints)
    {
        var byClass = endpoints.Items
            .GroupBy(static e => (e.Namespace, e.ClassName, e.ClassFqn, e.ClassPrefix, e.ClassTag))
            .ToList();

        foreach (var classGroup in byClass)
        {
            var (ns, className, fqn, prefix, tag) = classGroup.Key;
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("// ErrorOr endpoint mappings - NO REFLECTION, full AOT compatibility");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine($"namespace {ns};");
                sb.AppendLine();
            }

            var extensionClassName = $"{className}GeneratedExtensions";
            var baseName = className.Replace("Endpoints", "");
            var mapMethodName = $"Map{baseName}Endpoints";

            sb.AppendLine("/// <summary>");
            sb.AppendLine($"/// Generated endpoint mappings for <see cref=\"{className}\"/>.");
            sb.AppendLine("/// NO REFLECTION - all types resolved at compile time.");
            sb.AppendLine("/// </summary>");
            sb.AppendLine($"internal static class {extensionClassName}");
            sb.AppendLine("{");
            sb.AppendLine($"    public static global::Microsoft.AspNetCore.Routing.RouteGroupBuilder {mapMethodName}(");
            sb.AppendLine("        this global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)");
            sb.AppendLine("    {");

            var groupRoute = string.IsNullOrEmpty(prefix) ? "\"\"" : $"\"{prefix}\"";
            sb.AppendLine($"        var group = app.MapGroup({groupRoute});");
            sb.AppendLine();

            foreach (var endpoint in classGroup)
                EmitEndpointMapping(sb, endpoint, fqn);

            sb.AppendLine("        return group;");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            var fileName = string.IsNullOrEmpty(ns)
                ? $"{className}.Generated.g.cs"
                : $"{ns}.{className}.Generated.g.cs";

            spc.AddSource(fileName, SourceText.From(sb.ToString(), Encoding.UTF8));
        }
    }

    private static void EmitEndpointMapping(StringBuilder sb, EndpointInfo endpoint, string fqClassName)
    {
        var paramDecl = string.Join(", ", endpoint.Parameters.Items.Select(static p =>
            string.IsNullOrEmpty(p.Attributes)
                ? $"{p.Type} {p.Name}"
                : $"{p.Attributes} {p.Type} {p.Name}"));

        var paramCall = string.Join(", ", endpoint.Parameters.Items.Select(static p => p.Name));

        var methodCall = string.IsNullOrEmpty(paramCall)
            ? $"{fqClassName}.{endpoint.MethodName}()"
            : $"{fqClassName}.{endpoint.MethodName}({paramCall})";

        string lambda;
        if (endpoint.IsAsync)
            lambda = string.IsNullOrEmpty(paramDecl)
                ? $"async () => global::ErrorOr.Interceptors.Generated.ErrorOrHttp.Map(await {methodCall}, {endpoint.SuccessStatus})"
                : $"async ({paramDecl}) => global::ErrorOr.Interceptors.Generated.ErrorOrHttp.Map(await {methodCall}, {endpoint.SuccessStatus})";
        else
            lambda = string.IsNullOrEmpty(paramDecl)
                ? $"() => global::ErrorOr.Interceptors.Generated.ErrorOrHttp.Map({methodCall}, {endpoint.SuccessStatus})"
                : $"({paramDecl}) => global::ErrorOr.Interceptors.Generated.ErrorOrHttp.Map({methodCall}, {endpoint.SuccessStatus})";

        sb.AppendLine($"        group.Map{endpoint.HttpMethod}(\"{endpoint.Route}\", {lambda})");

        // Success response
        if (endpoint.SuccessStatus == 204)
            sb.AppendLine("            .Produces(204)");
        else
            sb.AppendLine($"            .Produces<{endpoint.ValueType}>({endpoint.SuccessStatus})");

        // Error responses
        sb.AppendLine("            .ProducesValidationProblem()");
        sb.AppendLine("            .ProducesProblem(404)");
        sb.AppendLine("            .ProducesProblem(401)");
        sb.AppendLine("            .ProducesProblem(403)");
        sb.AppendLine("            .ProducesProblem(409)");
        sb.AppendLine("            .ProducesProblem(500)");

        // Name
        sb.AppendLine(!string.IsNullOrEmpty(endpoint.EndpointName)
            ? $"            .WithName(\"{endpoint.EndpointName}\")"
            : $"            .WithName(\"{endpoint.MethodName}\")");

        // Tag
        if (!string.IsNullOrEmpty(endpoint.ClassTag))
            sb.AppendLine($"            .WithTags(\"{endpoint.ClassTag}\")");

        // Summary
        if (!string.IsNullOrEmpty(endpoint.Summary))
            sb.AppendLine($"            .WithSummary(\"{endpoint.Summary}\")");

        sb.AppendLine("            ;");
        sb.AppendLine();
    }
}
