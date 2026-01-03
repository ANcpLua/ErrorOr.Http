using System.Collections.Immutable;
using System.Text.RegularExpressions;
using ErrorOr.Http.Helpers;
using Microsoft.CodeAnalysis;

namespace ErrorOr.Http.Generators;

/// <summary>
///     Validates route patterns and parameters at compile time.
/// </summary>
internal static class RouteValidator
{
    // Matches {paramName} or {paramName:constraint} or {paramName:constraint(arg)} or {*catchAll}
    private static readonly Regex RouteParameterRegexInstance = new(
        @"\{(?<star>\*)?(?<name>[a-zA-Z_][a-zA-Z0-9_]*)(?::(?<constraint>[a-zA-Z]+)(?:\([^)]*\))?)?(?<optional>\?)?\}",
        RegexOptions.Compiled);

    /// <summary>
    ///     Extracts route parameters with their constraints from a route pattern.
    /// </summary>
    public static ImmutableArray<RouteParameterInfo> ExtractRouteParameters(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return ImmutableArray<RouteParameterInfo>.Empty;

        var matches = RouteParameterRegexInstance.Matches(pattern);
        if (matches.Count == 0)
            return ImmutableArray<RouteParameterInfo>.Empty;

        var builder = ImmutableArray.CreateBuilder<RouteParameterInfo>(matches.Count);

        foreach (Match match in matches)
        {
            var name = match.Groups["name"].Value;
            var constraint = match.Groups["constraint"].Success ? match.Groups["constraint"].Value : null;
            var isOptional = match.Groups["optional"].Success;
            var isCatchAll = match.Groups["star"].Success;

            builder.Add(new RouteParameterInfo(name, constraint, isOptional, isCatchAll));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Validates a route pattern and returns diagnostics for any issues.
    /// </summary>
    public static ImmutableArray<EndpointDiagnostic> ValidatePattern(
        string pattern,
        IMethodSymbol method,
        string attributeName)
    {
        var diagnostics = ImmutableArray.CreateBuilder<EndpointDiagnostic>();

        // Check for empty pattern
        if (string.IsNullOrWhiteSpace(pattern))
        {
            diagnostics.Add(EndpointDiagnostic.Create(
                DiagnosticDescriptors.InvalidRoutePattern,
                method,
                pattern,
                "Route pattern cannot be empty"));
            return diagnostics.ToImmutable();
        }

        // Check for leading slash (warning, not error - ASP.NET normalizes this)
        // We don't enforce this as ASP.NET Core handles it, but empty {} is an error

        // Check for empty parameter names: {}
        if (pattern.Contains("{}"))
        {
            diagnostics.Add(EndpointDiagnostic.Create(
                DiagnosticDescriptors.InvalidRoutePattern,
                method,
                pattern,
                "Route contains empty parameter '{}'. Parameter names are required."));
        }

        // Check for unclosed braces
        var openCount = pattern.Count(c => c == '{');
        var closeCount = pattern.Count(c => c == '}');
        if (openCount != closeCount)
        {
            diagnostics.Add(EndpointDiagnostic.Create(
                DiagnosticDescriptors.InvalidRoutePattern,
                method,
                pattern,
                $"Route has mismatched braces: {openCount} '{{' and {closeCount} '}}'"));
        }

        // Check for duplicate parameter names
        var routeParams = ExtractRouteParameters(pattern);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var param in routeParams)
        {
            if (!seen.Add(param.Name))
            {
                diagnostics.Add(EndpointDiagnostic.Create(
                    DiagnosticDescriptors.InvalidRoutePattern,
                    method,
                    pattern,
                    $"Route contains duplicate parameter '{{{param.Name}}}'"));
            }
        }

        return diagnostics.ToImmutable();
    }

    /// <summary>
    ///     Validates that all route parameters are bound to method parameters.
    /// </summary>
    public static ImmutableArray<EndpointDiagnostic> ValidateParameterBindings(
        string pattern,
        ImmutableArray<RouteParameterInfo> routeParams,
        ImmutableArray<MethodParameterInfo> methodParams,
        IMethodSymbol method)
    {
        var diagnostics = ImmutableArray.CreateBuilder<EndpointDiagnostic>();

        // Build lookup of method parameters by their bound route name
        var boundRouteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mp in methodParams)
        {
            if (mp.BoundRouteName is not null)
                boundRouteNames.Add(mp.BoundRouteName);
        }

        // Check each route parameter is bound
        foreach (var rp in routeParams)
        {
            if (!boundRouteNames.Contains(rp.Name))
            {
                diagnostics.Add(EndpointDiagnostic.Create(
                    DiagnosticDescriptors.RouteParameterNotBound,
                    method,
                    pattern,
                    rp.Name));
            }
        }

        return diagnostics.ToImmutable();
    }

    /// <summary>
    ///     Validates route constraint matches parameter type.
    /// </summary>
    public static EndpointDiagnostic? ValidateConstraintTypeMatch(
        RouteParameterInfo routeParam,
        ITypeSymbol parameterType,
        IMethodSymbol method)
    {
        if (routeParam.Constraint is null)
            return null;

        var expectedType = GetExpectedTypeForConstraint(routeParam.Constraint);
        if (expectedType is null)
            return null; // Unknown constraint, can't validate

        var actualType = GetTypeCategory(parameterType);
        if (actualType == expectedType)
            return null; // Types match

        return EndpointDiagnostic.Create(
            DiagnosticDescriptors.RouteConstraintTypeMismatch,
            method,
            routeParam.Name,
            routeParam.Constraint,
            parameterType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
    }

    private static string? GetExpectedTypeForConstraint(string constraint)
    {
        return constraint.ToLowerInvariant() switch
        {
            "int" => "int",
            "long" => "long",
            "float" => "float",
            "double" => "double",
            "decimal" => "decimal",
            "bool" => "bool",
            "guid" => "guid",
            "datetime" => "datetime",
            "alpha" => "string",
            "regex" => "string",
            "required" => null, // Any type
            "min" => "numeric",
            "max" => "numeric",
            "range" => "numeric",
            "minlength" => "string",
            "maxlength" => "string",
            "length" => "string",
            _ => null
        };
    }

    private static string GetTypeCategory(ITypeSymbol type)
    {
        var fqn = type.ToDisplayString();

        return fqn switch
        {
            "int" or "System.Int32" => "int",
            "long" or "System.Int64" => "long",
            "float" or "System.Single" => "float",
            "double" or "System.Double" => "double",
            "decimal" or "System.Decimal" => "decimal",
            "bool" or "System.Boolean" => "bool",
            "System.Guid" => "guid",
            "System.DateTime" => "datetime",
            "string" or "System.String" => "string",
            "short" or "System.Int16" or "byte" or "System.Byte" => "numeric",
            _ => "unknown"
        };
    }
}

/// <summary>
///     Information about a route parameter extracted from the route template.
/// </summary>
internal readonly record struct RouteParameterInfo(
    string Name,
    string? Constraint,
    bool IsOptional,
    bool IsCatchAll);

/// <summary>
///     Information about a method parameter relevant to route binding.
/// </summary>
internal readonly record struct MethodParameterInfo(
    string Name,
    string? BoundRouteName,
    ITypeSymbol Type);
