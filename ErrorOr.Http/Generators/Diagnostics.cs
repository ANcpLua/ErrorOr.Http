using ErrorOr.Http.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ErrorOr.Http.Generators;

internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor UnsupportedParameter = new(
        "EOE003",
        "Unsupported endpoint parameter",
        "Parameter '{0}' on '{1}' cannot be bound: {2}",
        "Usage",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor AmbiguousParameter = new(
        "EOE004",
        "Ambiguous endpoint parameter",
        "Parameter '{0}' on '{1}' is ambiguous. You must explicitly use [FromServices], [FromBody], [FromQuery], [FromHeader], or [FromKeyedServices]. Implicit service binding is disabled for safety.",
        "Usage",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor MultipleBodyParameters = new(
        "EOE005",
        "Multiple body parameters",
        "Endpoint '{0}' has multiple [FromBody] parameters. Only one is supported.",
        "Usage",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor MultipleBodySources = new(
        "EOE006",
        "Multiple body sources",
        "Endpoint '{0}' has multiple body sources ([FromBody], [FromForm], Stream, or PipeReader). Only one is allowed as they all consume the request body.",
        "Usage",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor MultipleFromFormParameters = new(
        "EOE007",
        "Multiple [FromForm] DTOs",
        "Endpoint '{0}' has multiple [FromForm] DTO parameters. Only one structured form DTO is allowed per endpoint.",
        "Usage",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor UnsupportedFormDtoShape = new(
        "EOE008",
        "Unsupported [FromForm] DTO shape",
        "[FromForm] parameter '{0}' on '{1}' has unsupported shape: {2}",
        "Usage",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor FormFileNotNullable = new(
        "EOE009",
        "IFormFile nullability",
        "IFormFile parameter '{0}' is non-nullable but file may be missing. Use IFormFile? for optional files.",
        "Usage",
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor FormContentTypeRequired = new(
        "EOE010",
        "Form content type required",
        "Endpoint '{0}' uses form binding but may receive non-form requests at runtime",
        "Usage",
        DiagnosticSeverity.Info,
        true);

    /// <summary>
    ///     EOE011: Multiple [FromForm] complex type parameters detected.
    ///     Only one structured form DTO can consume the form body.
    /// </summary>
    public static readonly DiagnosticDescriptor MultipleFormDtos = new(
        "EOE011",
        "Multiple [FromForm] DTOs",
        "Endpoint '{0}' has multiple [FromForm] complex type parameters. Only one structured form body is allowed.",
        "Usage",
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    ///     EOE012: Reserved for backwards compatibility (was FormCollectionNotSupported).
    /// </summary>
    [Obsolete("Use EOE013 FormCollectionRequiresAttribute instead")]
    public static readonly DiagnosticDescriptor FormCollectionNotSupported = new(
        "EOE012",
        "IFormCollection not yet supported",
        "Parameter '{0}' on '{1}' uses IFormCollection which is not yet supported. Use [FromForm] with a DTO type instead for type-safe form binding.",
        "Usage",
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    ///     EOE013: IFormCollection requires explicit [FromForm] attribute.
    ///     Unlike IFormFile which auto-binds, IFormCollection must be explicit.
    /// </summary>
    public static readonly DiagnosticDescriptor FormCollectionRequiresAttribute = new(
        "EOE013",
        "IFormCollection requires [FromForm]",
        "Parameter '{0}' on '{1}' is IFormCollection but lacks [FromForm]. IFormCollection does not auto-bind—add [FromForm] explicitly.",
        "Usage",
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    ///     EOE014: Type cannot be form-bound.
    ///     Catch-all for unsupported form binding scenarios.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedFormType = new(
        "EOE014",
        "Unsupported form binding type",
        "Parameter '{0}' on '{1}' cannot be form-bound. Type '{2}' is not a primitive, IParsable<T>, IFormFile, IFormFileCollection, or valid DTO.",
        "Usage",
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    ///     EOE021: Error type returned in code but not documented in OpenAPI metadata.
    ///     Fired when Error.XXX() is detected in method body but the corresponding
    ///     HTTP status code is not in the endpoint's Produces metadata.
    /// </summary>
    public static readonly DiagnosticDescriptor UndocumentedErrorResponse = new(
        "EOE021",
        "Undocumented error response",
        "Endpoint '{0}' returns Error.{1}() ({2}) but OpenAPI metadata only documents: {3}. " +
        "This error path will not appear in generated API documentation.",
        "OpenAPI",
        DiagnosticSeverity.Warning,
        true);

    /// <summary>
    ///     EOE022: Type used in endpoint not registered in JsonSerializerContext.
    ///     Critical for NativeAOT - unregistered types will fail at runtime.
    /// </summary>
    public static readonly DiagnosticDescriptor TypeNotInJsonContext = new(
        "EOE022",
        "Type not AOT-serializable",
        "Type '{0}' is used in endpoint '{1}' but not found in any [JsonSerializable] context. " +
        "Add [JsonSerializable(typeof({0}))] to your JsonSerializerContext for NativeAOT support.",
        "AOT",
        DiagnosticSeverity.Warning,
        true);
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
            : new EquatableArray<string>([..messageArgs]);

        return new EndpointDiagnostic(descriptor, args, LocationInfo.From(symbol.Locations.FirstOrDefault()));
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
