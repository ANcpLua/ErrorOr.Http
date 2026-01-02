using System.Collections.Immutable;
using ErrorOr.Http.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ErrorOr.Http.Generators;

/// <summary>
///     EOE021-EOE022: Analyzer partial for OpenAPI & AOT validation.
///     Runs during source generation to detect:
///     - EOE021: Undocumented error responses (error paths not in OpenAPI metadata)
///     - EOE022: Types not registered in JsonSerializerContext (AOT failures)
/// </summary>
public sealed partial class ErrorOrEndpointGenerator
{
    /// <summary>
    ///     Analyzes endpoints and user-defined JsonSerializerContext classes
    ///     to detect types that won't serialize in NativeAOT scenarios.
    /// </summary>
    private static void AnalyzeJsonContextCoverage(
        SourceProductionContext spc,
        ImmutableArray<EndpointDescriptor> endpoints,
        ImmutableArray<JsonContextInfo> userContexts)
    {
        if (endpoints.IsDefaultOrEmpty)
            return;

        var neededTypes = new HashSet<string>();
        var typeToEndpoint = new Dictionary<string, string>();

        foreach (var ep in endpoints)
        {
            if (!string.IsNullOrEmpty(ep.SuccessTypeFqn) &&
                ep.SuccessTypeFqn != "global::ErrorOr.Deleted" &&
                ep.SuccessTypeFqn != "global::ErrorOr.Updated" &&
                ep.SuccessTypeFqn != "global::ErrorOr.Created" &&
                ep.SuccessTypeFqn != "global::ErrorOr.Success")
            {
                if (neededTypes.Add(ep.SuccessTypeFqn))
                    typeToEndpoint[ep.SuccessTypeFqn] = ep.HandlerMethodName;
            }

            foreach (var param in ep.HandlerParameters.Items)
            {
                if (param.Source == EndpointParameterSource.Body)
                {
                    if (neededTypes.Add(param.TypeFqn))
                        typeToEndpoint[param.TypeFqn] = ep.HandlerMethodName;
                }
            }
        }

        neededTypes.Add(WellKnownTypes.Fqn.ProblemDetails);
        neededTypes.Add(WellKnownTypes.Fqn.HttpValidationProblemDetails);

        if (userContexts.IsDefaultOrEmpty)
            return;

        var registeredTypes = new HashSet<string>();
        foreach (var ctx in userContexts)
        {
            foreach (var typeFqn in ctx.SerializableTypes.Items)
                registeredTypes.Add(typeFqn);
        }

        foreach (var neededType in neededTypes)
        {
            var isRegistered = registeredTypes.Any(rt => TypeNamesMatch(neededType, rt));

            if (!isRegistered)
            {
                var displayType = neededType.Replace("global::", "");
                var endpointName = typeToEndpoint.TryGetValue(neededType, out var epName)
                    ? epName
                    : "ErrorOr endpoints";

                spc.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.TypeNotInJsonContext,
                    Location.None,
                    displayType,
                    endpointName));
            }
        }
    }

    /// <summary>
    ///     Checks if two type names refer to the same type.
    ///     Handles differences in global:: prefix and short vs full names.
    /// </summary>
    private static bool TypeNamesMatch(string needed, string registered)
    {
        var normalizedNeeded = needed.Replace("global::", "").Trim();
        var normalizedRegistered = registered.Replace("global::", "").Trim();

        if (normalizedNeeded == normalizedRegistered)
            return true;

        var neededShort = GetShortTypeName(normalizedNeeded);
        var registeredShort = GetShortTypeName(normalizedRegistered);

        if (neededShort == registeredShort)
            return true;

        if (normalizedNeeded.EndsWith(registeredShort) || normalizedRegistered.EndsWith(neededShort))
            return true;

        return false;
    }

    private static string GetShortTypeName(string typeName)
    {
        var isArray = typeName.EndsWith("[]");
        var baseName = isArray ? typeName[..^2] : typeName;

        var lastDot = baseName.LastIndexOf('.');
        var shortName = lastDot >= 0 ? baseName[(lastDot + 1)..] : baseName;

        return isArray ? shortName + "[]" : shortName;
    }
}

/// <summary>
///     Information about a user-defined JsonSerializerContext.
/// </summary>
internal readonly record struct JsonContextInfo(
    string ClassName,
    EquatableArray<string> SerializableTypes);

/// <summary>
///     Provider for finding JsonSerializerContext classes.
/// </summary>
internal static class JsonContextProvider
{
    /// <summary>
    ///     Creates an incremental provider that finds all user-defined JsonSerializerContext classes.
    /// </summary>
    // Suppress EPS06: IncrementalValuesProvider is a struct with fluent API designed for method chaining
#pragma warning disable EPS06
    public static IncrementalValuesProvider<JsonContextInfo> Create(IncrementalGeneratorInitializationContext context)
    {
        return context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (ctx, _) => TransformJsonContext(ctx))
            .Where(static info => info.HasValue)
            .Select(static (info, _) => info!.Value);
    }
#pragma warning restore EPS06

    private static JsonContextInfo? TransformJsonContext(GeneratorSyntaxContext ctx)
    {
        if (ctx.Node is not ClassDeclarationSyntax classDecl)
            return null;

        if (ctx.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol classSymbol)
            return null;

        if (!InheritsFromJsonSerializerContext(classSymbol))
            return null;

        var serializableTypes = new List<string>();

        foreach (var attr in classSymbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != WellKnownTypes.JsonSerializableAttribute)
                continue;

            if (attr.ConstructorArguments is { Length: >= 1 } args &&
                args[0].Value is ITypeSymbol typeArg)
            {
                var typeFqn = "global::" + typeArg.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Replace("global::", "");
                serializableTypes.Add(typeFqn);
            }
        }

        if (serializableTypes.Count == 0)
            return null;

        return new JsonContextInfo(
            classSymbol.Name,
            new EquatableArray<string>([.. serializableTypes]));
    }

    private static bool InheritsFromJsonSerializerContext(INamedTypeSymbol symbol)
    {
        var current = symbol.BaseType;
        while (current is not null)
        {
            if (current.ToDisplayString() == WellKnownTypes.JsonSerializerContext)
                return true;
            current = current.BaseType;
        }

        return false;
    }
}
