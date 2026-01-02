using System.Linq;
using ErrorOr.Http.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ErrorOr.Http.Generators;

#region Diagnostic Descriptors

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
        "Endpoint '{0}' has both [FromBody] and [FromForm] parameters. Only one body source is allowed.",
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
}

#endregion

#region Endpoint Diagnostic

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

        var args = MessageArgs.Items.Select(static value => (object?)value).ToArray();
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

#endregion
