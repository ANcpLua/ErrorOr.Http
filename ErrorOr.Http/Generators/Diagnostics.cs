using ErrorOr.Http.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ErrorOr.Http.Generators;

/// <summary>
///     Diagnostic descriptors for ErrorOr.Http source generator.
///     All diagnostics use the "EOE" prefix (ErrorOr Endpoint).
/// </summary>
internal static class DiagnosticDescriptors
{
    // ─────────────────────────────────────────────────────────────────────────
    // Parameter Binding Errors (EOE003-EOE005)
    // ─────────────────────────────────────────────────────────────────────────

    public static readonly DiagnosticDescriptor UnsupportedParameter = new(
        "EOE003", "Unsupported parameter",
        "Parameter '{0}' on '{1}' cannot be bound: {2}",
        "Usage", DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor AmbiguousParameter = new(
        "EOE004", "Ambiguous parameter",
        "Parameter '{0}' on '{1}' is ambiguous. Use [FromServices], [FromBody], [FromQuery], [FromHeader], or [FromKeyedServices].",
        "Usage", DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor MultipleBodyParameters = new(
        "EOE005", "Multiple body parameters",
        "Endpoint '{0}' has multiple [FromBody] parameters. Only one is supported.",
        "Usage", DiagnosticSeverity.Error, true);

    // ─────────────────────────────────────────────────────────────────────────
    // Body Source Conflicts (EOE006-EOE008)
    // ─────────────────────────────────────────────────────────────────────────

    public static readonly DiagnosticDescriptor MultipleBodySources = new(
        "EOE006", "Multiple body sources",
        "Endpoint '{0}' has multiple body sources ([FromBody], [FromForm], Stream, PipeReader). Only one allowed.",
        "Usage", DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor MultipleFromFormParameters = new(
        "EOE007", "Multiple [FromForm] DTOs",
        "Endpoint '{0}' has multiple [FromForm] DTO parameters. Only one structured form DTO allowed.",
        "Usage", DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor UnsupportedFormDtoShape = new(
        "EOE008", "Unsupported form DTO",
        "[FromForm] parameter '{0}' on '{1}' has unsupported shape: {2}",
        "Usage", DiagnosticSeverity.Error, true);

    // ─────────────────────────────────────────────────────────────────────────
    // Form Binding (EOE009-EOE014)
    // ─────────────────────────────────────────────────────────────────────────

    public static readonly DiagnosticDescriptor FormFileNotNullable = new(
        "EOE009", "IFormFile should be nullable",
        "IFormFile parameter '{0}' is non-nullable but file uploads are optional. Use IFormFile? instead.",
        "Usage", DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor FormContentTypeRequired = new(
        "EOE010", "Form content type required",
        "Endpoint '{0}' uses form binding. Ensure clients send multipart/form-data or application/x-www-form-urlencoded.",
        "Usage", DiagnosticSeverity.Info, true);

    public static readonly DiagnosticDescriptor MultipleFormDtos = new(
        "EOE011", "Multiple form DTOs",
        "Endpoint '{0}' has multiple [FromForm] complex type parameters. Only one structured form body allowed.",
        "Usage", DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor FormCollectionRequiresAttribute = new(
        "EOE013", "IFormCollection requires [FromForm]",
        "Parameter '{0}' on '{1}' is IFormCollection but lacks [FromForm]. Add [FromForm] explicitly.",
        "Usage", DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor UnsupportedFormType = new(
        "EOE014", "Unsupported form type",
        "Parameter '{0}' on '{1}' cannot be form-bound. Type '{2}' is not a primitive, IParsable<T>, IFormFile, or valid DTO.",
        "Usage", DiagnosticSeverity.Error, true);

    // ─────────────────────────────────────────────────────────────────────────
    // OpenAPI & AOT (EOE021-EOE022)
    // ─────────────────────────────────────────────────────────────────────────

    public static readonly DiagnosticDescriptor UndocumentedErrorResponse = new(
        "EOE021", "Undocumented error response",
        "Endpoint '{0}' returns Error.{1}() but OpenAPI metadata only documents: {2}",
        "OpenAPI", DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor TypeNotInJsonContext = new(
        "EOE022", "Type not AOT-serializable",
        "Type '{0}' in endpoint '{1}' not found in any [JsonSerializable] context. Add it for NativeAOT support.",
        "AOT", DiagnosticSeverity.Warning, true);
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
