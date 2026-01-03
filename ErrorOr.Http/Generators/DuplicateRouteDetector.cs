using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace ErrorOr.Http.Generators;

/// <summary>
///     Detects duplicate routes across all registered endpoints.
///     Runs during the final aggregation phase.
/// </summary>
internal static class DuplicateRouteDetector
{
    /// <summary>
    ///     Checks for duplicate routes and endpoint name collisions.
    /// </summary>
    public static ImmutableArray<Diagnostic> Detect(ImmutableArray<EndpointDescriptor> endpoints)
    {
        if (endpoints.IsDefaultOrEmpty || endpoints.Length < 2)
            return ImmutableArray<Diagnostic>.Empty;

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        DetectDuplicateRoutes(endpoints, diagnostics);
        DetectEndpointNameCollisions(endpoints, diagnostics);

        return diagnostics.ToImmutable();
    }

    private static void DetectDuplicateRoutes(
        ImmutableArray<EndpointDescriptor> endpoints,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        // Key: normalized "METHOD /pattern"
        var routeMap = new Dictionary<string, EndpointDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var ep in endpoints)
        {
            var normalizedPattern = NormalizeRoutePattern(ep.Pattern);
            var key = $"{ep.HttpMethod.ToUpperInvariant()} {normalizedPattern}";

            if (routeMap.TryGetValue(key, out var existing))
            {
                // Duplicate found
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.DuplicateRoute,
                    Location.None, // Would need location info in EndpointDescriptor
                    ep.HttpMethod.ToUpperInvariant(),
                    ep.Pattern,
                    ExtractTypeName(existing.HandlerContainingTypeFqn),
                    existing.HandlerMethodName));
            }
            else
            {
                routeMap[key] = ep;
            }
        }
    }

    private static void DetectEndpointNameCollisions(
        ImmutableArray<EndpointDescriptor> endpoints,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        // Key: operation ID (ClassName_MethodName)
        var nameMap = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var ep in endpoints)
        {
            var className = ExtractTypeName(ep.HandlerContainingTypeFqn);
            var operationId = $"{className}_{ep.HandlerMethodName}";

            if (nameMap.TryGetValue(operationId, out var count))
            {
                nameMap[operationId] = count + 1;
            }
            else
            {
                nameMap[operationId] = 1;
            }
        }

        foreach (var kvp in nameMap)
        {
            if (kvp.Value > 1)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.EndpointNameCollision,
                    Location.None,
                    kvp.Key));
            }
        }
    }

    /// <summary>
    ///     Normalizes route patterns for duplicate detection.
    ///     Replaces parameter names with placeholders since {id} and {userId} are structurally equivalent.
    /// </summary>
    private static string NormalizeRoutePattern(string pattern)
    {
        // Replace {anything} with {_} for comparison
        // This catches /users/{id} vs /users/{userId} as duplicates
        var normalized = System.Text.RegularExpressions.Regex.Replace(
            pattern,
            @"\{[^}]+\}",
            "{_}");

        // Ensure leading slash
        if (!normalized.StartsWith("/"))
            normalized = "/" + normalized;

        // Remove trailing slash for consistency
        if (normalized.Length > 1 && normalized.EndsWith("/"))
            normalized = normalized[..^1];

        return normalized;
    }

    private static string ExtractTypeName(string fqn)
    {
        var lastDot = fqn.LastIndexOf('.');
        var name = lastDot >= 0 ? fqn[(lastDot + 1)..] : fqn;

        // Handle global:: prefix
        if (name.StartsWith("::"))
            name = name[2..];

        return name;
    }
}
