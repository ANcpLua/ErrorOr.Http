using ErrorOr.Http.Helpers;
using Microsoft.CodeAnalysis;

namespace ErrorOr.Http.Generators;

#region Endpoint Types

internal enum EndpointParameterSource
{
    Route,
    Body,
    Query,
    Header,
    Service,
    KeyedService,
    AsParameters,
    HttpContext,
    CancellationToken
}

internal readonly record struct EndpointParameter(
    string Name,
    string TypeFqn,
    EndpointParameterSource Source,
    string? KeyName,
    bool IsNullable,
    bool IsNonNullableValueType,
    bool IsCollection,
    string? CollectionItemTypeFqn,
    EquatableArray<EndpointParameter> Children);

internal readonly record struct EndpointDescriptor(
    string HttpMethod,
    string Pattern,
    string SuccessTypeFqn,
    bool IsAsync,
    string HandlerContainingTypeFqn,
    string HandlerMethodName,
    bool IsObsolete,
    string? ObsoleteMessage,
    bool IsObsoleteError,
    EquatableArray<EndpointParameter> HandlerParameters,
    EquatableArray<int> InferredErrorTypes);

internal readonly record struct EndpointData(
    EquatableArray<EndpointDescriptor> Descriptors,
    EquatableArray<EndpointDiagnostic> Diagnostics)
{
    public static EndpointData Empty
    {
        get => new(EquatableArray<EndpointDescriptor>.Empty, EquatableArray<EndpointDiagnostic>.Empty);
    }
}

#endregion

#region Parameter Meta

internal readonly record struct ParameterMeta(
    int Index,
    IParameterSymbol Symbol,
    string Name,
    string TypeFqn,
    RoutePrimitiveKind? RouteKind,
    bool HasFromServices,
    bool HasFromKeyedServices,
    string? KeyedServiceKey,
    bool HasFromBody,
    bool HasFromRoute,
    bool HasFromQuery,
    bool HasFromHeader,
    bool HasAsParameters,
    string RouteName,
    string QueryName,
    string HeaderName,
    bool IsCancellationToken,
    bool IsHttpContext,
    bool IsNullable,
    bool IsNonNullableValueType,
    bool IsCollection,
    string? CollectionItemTypeFqn,
    RoutePrimitiveKind? CollectionItemPrimitiveKind);

internal enum RoutePrimitiveKind
{
    String,
    Int32,
    Int64,
    Int16,
    UInt32,
    UInt64,
    UInt16,
    Byte,
    SByte,
    Boolean,
    Decimal,
    Double,
    Single,
    Guid,
    DateTime,
    DateTimeOffset,
    DateOnly,
    TimeOnly,
    TimeSpan
}

#endregion
