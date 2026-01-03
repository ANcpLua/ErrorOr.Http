using ErrorOr.Http.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ErrorOr.Http.Generators;

/// <summary>
///     Diagnostic descriptors for ErrorOr.Http source generator.
///     All diagnostics use the "EOE" prefix (ErrorOr Endpoint).
/// </summary>
/// <remarks>
///     ID Ranges:
///     - EOE001-EOE002: Handler validation
///     - EOE003-EOE005: Parameter binding errors
///     - EOE006-EOE008: Body source conflicts
///     - EOE009-EOE014: Form binding
///     - EOE015-EOE020: Route validation
///     - EOE021-EOE025: OpenAPI and AOT
/// </remarks>
internal static class DiagnosticDescriptors
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Handler Validation (EOE001-EOE002)
    // ═══════════════════════════════════════════════════════════════════════════

    public static readonly DiagnosticDescriptor InvalidReturnType = new(
        "EOE001",
        "Invalid return type",
        "Method '{0}' is marked with [{1}] but does not return ErrorOr<T>, Task<ErrorOr<T>>, or ValueTask<ErrorOr<T>>",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NonStaticHandler = new(
        "EOE002",
        "Handler must be static",
        "Method '{0}' is marked with [{1}] but is not static. ErrorOr.Http only supports static handler methods.",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // ═══════════════════════════════════════════════════════════════════════════
    // Parameter Binding Errors (EOE003-EOE005)
    // ═══════════════════════════════════════════════════════════════════════════

    public static readonly DiagnosticDescriptor UnsupportedParameter = new(
        "EOE003",
        "Unsupported parameter",
        "Parameter '{0}' on '{1}' cannot be bound: {2}",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AmbiguousParameter = new(
        "EOE004",
        "Ambiguous parameter",
        "Parameter '{0}' on '{1}' is ambiguous. Use [FromServices], [FromBody], [FromQuery], [FromRoute], [FromHeader], or [FromKeyedServices].",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MultipleBodyParameters = new(
        "EOE005",
        "Multiple body parameters",
        "Endpoint '{0}' has multiple [FromBody] parameters. Only one is supported.",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // ═══════════════════════════════════════════════════════════════════════════
    // Body Source Conflicts (EOE006-EOE008)
    // ═══════════════════════════════════════════════════════════════════════════

    public static readonly DiagnosticDescriptor MultipleBodySources = new(
        "EOE006",
        "Multiple body sources",
        "Endpoint '{0}' has multiple body sources ([FromBody], [FromForm], Stream, PipeReader). Only one allowed.",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MultipleFromFormParameters = new(
        "EOE007",
        "Multiple [FromForm] DTOs",
        "Endpoint '{0}' has multiple [FromForm] DTO parameters. Only one structured form DTO allowed.",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedFormDtoShape = new(
        "EOE008",
        "Unsupported form DTO",
        "[FromForm] parameter '{0}' on '{1}' has unsupported shape: {2}",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // ═══════════════════════════════════════════════════════════════════════════
    // Form Binding (EOE009-EOE014)
    // ═══════════════════════════════════════════════════════════════════════════

    public static readonly DiagnosticDescriptor FormFileNotNullable = new(
        "EOE009",
        "IFormFile should be nullable",
        "IFormFile parameter '{0}' is non-nullable but file uploads are optional. Use IFormFile? instead.",
        "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FormContentTypeRequired = new(
        "EOE010",
        "Form content type required",
        "Endpoint '{0}' uses form binding. Ensure clients send multipart/form-data or application/x-www-form-urlencoded.",
        "Usage",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FormCollectionRequiresAttribute = new(
        "EOE013",
        "IFormCollection requires [FromForm]",
        "Parameter '{0}' on '{1}' is IFormCollection but lacks [FromForm]. Add [FromForm] explicitly.",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedFormType = new(
        "EOE014",
        "Unsupported form type",
        "Parameter '{0}' on '{1}' cannot be form-bound. Type '{2}' is not a primitive, IParsable<T>, IFormFile, or valid DTO.",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // ═══════════════════════════════════════════════════════════════════════════
    // Route Validation (EOE015-EOE020)
    // ═══════════════════════════════════════════════════════════════════════════

    public static readonly DiagnosticDescriptor RouteParameterNotBound = new(
        "EOE015",
        "Route parameter not bound",
        "Route template '{0}' contains parameter '{{{1}}}' but no method parameter captures it. Add a parameter named '{1}' or use [FromRoute(Name = \"{1}\")].",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateRoute = new(
        "EOE016",
        "Duplicate route",
        "Route '{0} {1}' is already registered by '{2}.{3}'. Each route must have exactly one handler.",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidRoutePattern = new(
        "EOE017",
        "Invalid route pattern",
        "Route pattern '{0}' is invalid: {1}",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnboundRouteParameter = new(
        "EOE018",
        "Unbound route parameter",
        "Route template has parameter '{{{0}}}' but method parameter '{1}' does not match. Did you mean [FromRoute(Name = \"{0}\")]?",
        "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor EndpointNameCollision = new(
        "EOE019",
        "Endpoint name collision",
        "Multiple endpoints would have the same operation ID '{0}'. Use [EndpointName] to disambiguate for OpenAPI.",
        "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BodyOnReadOnlyMethod = new(
        "EOE020",
        "Body parameter on read-only HTTP method",
        "Endpoint '{0}' uses {1} with [FromBody]. GET, HEAD, DELETE, and OPTIONS should not have request bodies per HTTP semantics.",
        "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // ═══════════════════════════════════════════════════════════════════════════
    // OpenAPI & AOT (EOE021-EOE025)
    // ═══════════════════════════════════════════════════════════════════════════

    public static readonly DiagnosticDescriptor UndocumentedErrorResponse = new(
        "EOE021",
        "Undocumented error response",
        "Endpoint '{0}' returns Error.{1}() but OpenAPI metadata only documents: {2}",
        "OpenAPI",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TypeNotInJsonContext = new(
        "EOE022",
        "Type not AOT-serializable",
        "Type '{0}' in endpoint '{1}' not found in any [JsonSerializable] context. Add it for NativeAOT support.",
        "AOT",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RouteConstraintTypeMismatch = new(
        "EOE023",
        "Route constraint type mismatch",
        "Route parameter '{{{0}:{1}}}' has constraint '{1}' but method parameter is '{2}'. These types may not be compatible.",
        "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PrimitiveTypeInJsonContext = new(
        "EOE024",
        "Primitive type in JSON context warning",
        "Type '{0}' is a primitive and does not need explicit [JsonSerializable] registration.",
        "AOT",
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: false);

    public static readonly DiagnosticDescriptor SseErrorAfterStreamStart = new(
        "EOE025",
        "SSE error handling limitation",
        "Endpoint '{0}' returns IAsyncEnumerable<T>. Errors thrown during enumeration cannot be returned as ProblemDetails because HTTP headers are already sent.",
        "Usage",
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: true);
}

internal readonly record struct EndpointDiagnostic(
    DiagnosticDescriptor Descriptor,
    EquatableArray<string> MessageArgs,
    LocationInfo? SourceLocation)
{
    public static EndpointDiagnostic Create(
        DiagnosticDescriptor descriptor,
        ISymbol symbol,
        params string[] messageArgs)
    {
        var args = messageArgs.Length is 0
            ? EquatableArray<string>.Empty
            : new EquatableArray<string>([.. messageArgs]);

        return new EndpointDiagnostic(descriptor, args, LocationInfo.From(symbol.Locations.FirstOrDefault()));
    }

    public static EndpointDiagnostic Create(
        DiagnosticDescriptor descriptor,
        Location? location,
        params string[] messageArgs)
    {
        var args = messageArgs.Length is 0
            ? EquatableArray<string>.Empty
            : new EquatableArray<string>([.. messageArgs]);

        return new EndpointDiagnostic(descriptor, args, LocationInfo.From(location));
    }

    public Diagnostic ToDiagnostic()
    {
        var location = SourceLocation is { } info
            ? Location.Create(info.FilePath, info.Span, info.LineSpan)
            : Location.None;

        if (MessageArgs.IsDefaultOrEmpty)
            return Diagnostic.Create(Descriptor, location);

        var args = MessageArgs.Items.Select(static object? (value) => value).ToArray();
        return Diagnostic.Create(Descriptor, location, args);
    }
}

internal readonly record struct LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public static LocationInfo? From(Location? location)
    {
        if (location is null || location == Location.None || !location.IsInSource)
            return null;

        var lineSpan = location.GetLineSpan();
        return new LocationInfo(lineSpan.Path, location.SourceSpan, lineSpan.Span);
    }
}
