using System.Collections.Immutable;
using ErrorOr.Interceptors.Helpers;

namespace ErrorOr.Interceptors.Generators;

/// <summary>
/// Result of extracting endpoint info from a method.
/// Uses nullable pattern to avoid null-forgiving operators.
/// </summary>
internal readonly record struct ExtractResult(EndpointInfo? Endpoint = null);

/// <summary>
/// Represents a discovered endpoint method.
/// </summary>
internal readonly record struct EndpointInfo(
    string Namespace,
    string ClassName,
    string ClassFqn,
    string ClassPrefix,
    string ClassTag,
    string MethodName,
    string Route,
    string HttpMethod,
    int SuccessStatus,
    string ValueType,
    bool IsAsync,
    EquatableArray<ParameterInfo> Parameters,
    string? EndpointName,
    string? Summary);

/// <summary>
/// Represents a parameter of an endpoint method.
/// </summary>
internal readonly record struct ParameterInfo(
    string Name,
    string Type,
    string Attributes);
