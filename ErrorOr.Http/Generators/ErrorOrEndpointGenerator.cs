using System.Collections.Immutable;
using System.Text;
using ErrorOr.Http.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ErrorOr.Http.Generators;

/// <summary>
///     Source generator for ASP.NET Core Minimal API endpoints that return ErrorOr&lt;T&gt;.
///     Generates RequestDelegate implementations with OpenAPI metadata and ProblemDetails error handling.
/// </summary>
#pragma warning disable RS1041
[Generator(LanguageNames.CSharp)]
#pragma warning restore RS1041
public sealed partial class ErrorOrEndpointGenerator : IIncrementalGenerator
{
    private static class TrackingNames
    {
        public const string EndpointCollection = nameof(EndpointCollection);
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(EmitAttributeDefinition);

        // Create providers for each HTTP method attribute
        var getProvider = CreateAttributeProvider(context, WellKnownTypes.GetAttribute);
        var postProvider = CreateAttributeProvider(context, WellKnownTypes.PostAttribute);
        var putProvider = CreateAttributeProvider(context, WellKnownTypes.PutAttribute);
        var deleteProvider = CreateAttributeProvider(context, WellKnownTypes.DeleteAttribute);
        var patchProvider = CreateAttributeProvider(context, WellKnownTypes.PatchAttribute);
        var baseProvider = CreateAttributeProvider(context, WellKnownTypes.ErrorOrEndpointAttribute);

        // Register per-endpoint diagnostics
        RegisterDiagnostics(context, getProvider);
        RegisterDiagnostics(context, postProvider);
        RegisterDiagnostics(context, putProvider);
        RegisterDiagnostics(context, deleteProvider);
        RegisterDiagnostics(context, patchProvider);
        RegisterDiagnostics(context, baseProvider);

        // Combine all endpoints
        var endpoints = CombineProviders(
            getProvider, postProvider, putProvider,
            deleteProvider, patchProvider, baseProvider)
            .WithTrackingName(TrackingNames.EndpointCollection);

        // Cross-endpoint validation (duplicates, name collisions)
        context.RegisterSourceOutput(endpoints, static (spc, items) =>
        {
            if (items.IsDefaultOrEmpty)
                return;

            // Run cross-endpoint validations
            var crossDiagnostics = DuplicateRouteDetector.Detect(items);
            foreach (var diag in crossDiagnostics)
            {
                spc.ReportDiagnostic(diag);
            }

            // Emit the endpoint mappings
            EmitEndpoints(spc, items);
        });

        // JSON context analysis
        var jsonContexts = JsonContextProvider.Create(context).Collect();
        context.RegisterSourceOutput(
            endpoints.Combine(jsonContexts),
            static (spc, data) =>
            {
                var (eps, contexts) = data;
                if (!eps.IsDefaultOrEmpty)
                    AnalyzeJsonContextCoverage(spc, eps, contexts);
            });
    }

#pragma warning disable EPS06
    private static IncrementalValuesProvider<EndpointData> CreateAttributeProvider(
        IncrementalGeneratorInitializationContext context,
        string attributeName)
    {
        return context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeName,
                static (n, _) => n is MethodDeclarationSyntax,
                static (ctx, _) => ExtractAndValidateEndpoint(ctx))
            .Where(static data => !data.Descriptors.IsDefaultOrEmpty || !data.Diagnostics.IsDefaultOrEmpty);
    }

    private static IncrementalValueProvider<ImmutableArray<EndpointDescriptor>> CombineProviders(
        IncrementalValuesProvider<EndpointData> p1,
        IncrementalValuesProvider<EndpointData> p2,
        IncrementalValuesProvider<EndpointData> p3,
        IncrementalValuesProvider<EndpointData> p4,
        IncrementalValuesProvider<EndpointData> p5,
        IncrementalValuesProvider<EndpointData> p6)
    {
        return p1.SelectMany(static (d, _) => d.Descriptors.Items).Collect()
            .Combine(p2.SelectMany(static (d, _) => d.Descriptors.Items).Collect())
            .Combine(p3.SelectMany(static (d, _) => d.Descriptors.Items).Collect())
            .Combine(p4.SelectMany(static (d, _) => d.Descriptors.Items).Collect())
            .Combine(p5.SelectMany(static (d, _) => d.Descriptors.Items).Collect())
            .Combine(p6.SelectMany(static (d, _) => d.Descriptors.Items).Collect())
            .Select(static (combined, _) =>
            {
                var (((((e0, e1), e2), e3), e4), e5) = combined;
                var builder = ImmutableArray.CreateBuilder<EndpointDescriptor>(
                    e0.Length + e1.Length + e2.Length + e3.Length + e4.Length + e5.Length);
                builder.AddRange(e0);
                builder.AddRange(e1);
                builder.AddRange(e2);
                builder.AddRange(e3);
                builder.AddRange(e4);
                builder.AddRange(e5);
                return builder.ToImmutable();
            });
    }

    private static void RegisterDiagnostics(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<EndpointData> provider)
    {
        context.RegisterSourceOutput(
            provider.SelectMany(static (data, _) => data.Diagnostics.Items),
            static (spc, diagnostic) => spc.ReportDiagnostic(diagnostic.ToDiagnostic()));
    }
#pragma warning restore EPS06

    /// <summary>
    ///     Extracts endpoint data with full validation.
    ///     This is the main entry point that validates:
    ///     - Handler is static (EOE002)
    ///     - Return type is ErrorOr (EOE001)
    ///     - Route pattern is valid (EOE017)
    ///     - Route parameters are bound (EOE015)
    ///     - Body not on GET/HEAD/DELETE/OPTIONS (EOE020)
    /// </summary>
    private static EndpointData ExtractAndValidateEndpoint(GeneratorAttributeSyntaxContext ctx)
    {
        var diagnostics = ImmutableArray.CreateBuilder<EndpointDiagnostic>();

        // Get the method symbol
        if (ctx.TargetSymbol is not IMethodSymbol method)
            return EndpointData.Empty;

        // Get attribute name for error messages
        var attrName = ctx.Attributes.FirstOrDefault()?.AttributeClass?.Name ?? "ErrorOrEndpoint";
        if (attrName.EndsWith("Attribute"))
            attrName = attrName[..^"Attribute".Length];

        // EOE002: Handler must be static
        if (!method.IsStatic)
        {
            diagnostics.Add(EndpointDiagnostic.Create(
                DiagnosticDescriptors.NonStaticHandler,
                method,
                method.Name,
                attrName));
            return new EndpointData(
                EquatableArray<EndpointDescriptor>.Empty,
                new EquatableArray<EndpointDiagnostic>(diagnostics.ToImmutable()));
        }

        // EOE001: Return type must be ErrorOr<T>
        var returnTypeInfo = ExtractErrorOrReturnType(method.ReturnType);
        if (returnTypeInfo.SuccessTypeFqn is null)
        {
            diagnostics.Add(EndpointDiagnostic.Create(
                DiagnosticDescriptors.InvalidReturnType,
                method,
                method.Name,
                attrName));
            return new EndpointData(
                EquatableArray<EndpointDescriptor>.Empty,
                new EquatableArray<EndpointDiagnostic>(diagnostics.ToImmutable()));
        }

        // Process each attribute (method can have multiple [Get], [Post], etc.)
        var inferredErrors = InferErrorTypesFromMethod(ctx, method);
        var knownSymbols = KnownSymbols.Create(ctx.SemanticModel.Compilation);
        var descriptors = ImmutableArray.CreateBuilder<EndpointDescriptor>();

        foreach (var attr in ctx.Attributes)
        {
            if (!TryExtractRouteInfo(attr, out var httpMethod, out var pattern))
                continue;

            // EOE017: Validate route pattern syntax
            var patternDiagnostics = RouteValidator.ValidatePattern(pattern, method, attrName);
            foreach (var diag in patternDiagnostics)
            {
                diagnostics.Add(diag);
            }

            if (patternDiagnostics.Any(d => d.Descriptor.DefaultSeverity == DiagnosticSeverity.Error))
                continue; // Skip this endpoint if route is invalid

            // Extract route parameters with constraints
            var routeParams = RouteValidator.ExtractRouteParameters(pattern);
            var routeParamNames = routeParams.Select(p => p.Name).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

            // Bind method parameters
            var parameterResult = BindParameters(method, routeParamNames, diagnostics, knownSymbols);
            if (!parameterResult.IsValid)
                continue;

            // EOE015: Validate all route parameters are bound
            var methodParamInfos = parameterResult.Parameters
                .Select(p => new MethodParameterInfo(
                    p.Name,
                    p.Source == EndpointParameterSource.Route ? (p.KeyName ?? p.Name) : null,
                    null!)) // Type not needed for this check
                .ToImmutableArray();

            var bindingDiagnostics = RouteValidator.ValidateParameterBindings(
                pattern, routeParams, methodParamInfos, method);
            foreach (var diag in bindingDiagnostics)
            {
                diagnostics.Add(diag);
            }

            // EOE020: Body on read-only method
            var hasBody = parameterResult.Parameters.Any(p => p.Source == EndpointParameterSource.Body);
            if (hasBody && IsReadOnlyHttpMethod(httpMethod))
            {
                diagnostics.Add(EndpointDiagnostic.Create(
                    DiagnosticDescriptors.BodyOnReadOnlyMethod,
                    method,
                    method.Name,
                    httpMethod.ToUpperInvariant()));
            }

            // Note: EOE025 (SSE error handling limitation) is available but not emitted by default
            // since it's purely informational and developers have already chosen to use SSE.

            // Get obsolete info
            var (isObsolete, obsoleteMessage, isObsoleteError) = GetObsoleteInfo(method, knownSymbols);

            descriptors.Add(new EndpointDescriptor(
                httpMethod.ToUpperInvariant(), // Normalize case
                pattern,
                returnTypeInfo.SuccessTypeFqn,
                returnTypeInfo.IsAsync,
                method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                method.Name,
                isObsolete,
                obsoleteMessage,
                isObsoleteError,
                new EquatableArray<EndpointParameter>(parameterResult.Parameters),
                inferredErrors,
                returnTypeInfo.IsSse,
                returnTypeInfo.SseItemTypeFqn,
                returnTypeInfo.UsesSseItem));
        }

        return new EndpointData(
            new EquatableArray<EndpointDescriptor>(descriptors.ToImmutable()),
            new EquatableArray<EndpointDiagnostic>(diagnostics.ToImmutable()));
    }

    private static bool TryExtractRouteInfo(
        AttributeData attr,
        out string httpMethod,
        out string pattern)
    {
        httpMethod = string.Empty;
        pattern = string.Empty;

        var attrClass = attr.AttributeClass?.ToDisplayString();
        if (attrClass is null)
            return false;

        // Check for derived attributes
        var derivedMethod = attrClass switch
        {
            WellKnownTypes.GetAttribute => "GET",
            WellKnownTypes.PostAttribute => "POST",
            WellKnownTypes.PutAttribute => "PUT",
            WellKnownTypes.DeleteAttribute => "DELETE",
            WellKnownTypes.PatchAttribute => "PATCH",
            _ => null
        };

        if (derivedMethod is not null)
        {
            httpMethod = derivedMethod;
            pattern = attr.ConstructorArguments.Length > 0 &&
                      attr.ConstructorArguments[0].Value is string p &&
                      !string.IsNullOrWhiteSpace(p)
                ? p
                : "/";
            return true;
        }

        // Check for base attribute
        if (attrClass == WellKnownTypes.ErrorOrEndpointAttribute)
        {
            if (attr.ConstructorArguments.Length < 2)
                return false;

            if (attr.ConstructorArguments[0].Value is not string method ||
                attr.ConstructorArguments[1].Value is not string route ||
                string.IsNullOrWhiteSpace(method))
                return false;

            httpMethod = method;
            pattern = string.IsNullOrWhiteSpace(route) ? "/" : route;
            return true;
        }

        return false;
    }

    private static bool IsReadOnlyHttpMethod(string method)
    {
        return method.ToUpperInvariant() is "GET" or "HEAD" or "OPTIONS" or "DELETE";
    }

    private static void EmitAttributeDefinition(IncrementalGeneratorPostInitializationContext context)
    {
        context.AddSource("ErrorOrEndpointAttribute.g.cs", SourceText.From("""
            using System;

            namespace ErrorOr.Http
            {
                /// <summary>
                /// Base class for HTTP endpoint attributes.
                /// </summary>
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public abstract class ErrorOrEndpointAttribute : Attribute
                {
                    protected ErrorOrEndpointAttribute(string httpMethod, string pattern = "/")
                    {
                        HttpMethod = httpMethod;
                        Pattern = pattern;
                    }

                    public string HttpMethod { get; }
                    public string Pattern { get; }
                }

                /// <summary>HTTP GET endpoint.</summary>
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public sealed class GetAttribute : ErrorOrEndpointAttribute
                {
                    public GetAttribute(string pattern = "/") : base("GET", pattern) { }
                }

                /// <summary>HTTP POST endpoint.</summary>
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public sealed class PostAttribute : ErrorOrEndpointAttribute
                {
                    public PostAttribute(string pattern = "/") : base("POST", pattern) { }
                }

                /// <summary>HTTP PUT endpoint.</summary>
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public sealed class PutAttribute : ErrorOrEndpointAttribute
                {
                    public PutAttribute(string pattern = "/") : base("PUT", pattern) { }
                }

                /// <summary>HTTP DELETE endpoint.</summary>
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public sealed class DeleteAttribute : ErrorOrEndpointAttribute
                {
                    public DeleteAttribute(string pattern = "/") : base("DELETE", pattern) { }
                }

                /// <summary>HTTP PATCH endpoint.</summary>
                [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                public sealed class PatchAttribute : ErrorOrEndpointAttribute
                {
                    public PatchAttribute(string pattern = "/") : base("PATCH", pattern) { }
                }
            }
            """, Encoding.UTF8));
    }
}
