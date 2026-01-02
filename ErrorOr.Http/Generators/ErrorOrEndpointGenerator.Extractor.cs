using System.Collections.Immutable;
using System.Text.RegularExpressions;
using ErrorOr.Http.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ErrorOr.Http.Generators;

/// <summary>
///     Partial class containing all extraction logic for the endpoint generator.
/// </summary>
public sealed partial class ErrorOrEndpointGenerator
{

    internal static EndpointData ExtractEndpointData(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not IMethodSymbol { IsStatic: true } method)
            return EndpointData.Empty;

        var returnTypeInfo = ExtractErrorOrReturnType(method.ReturnType);
        if (returnTypeInfo.SuccessTypeFqn is null)
            return EndpointData.Empty;

        var inferredErrors = InferErrorTypesFromMethod(ctx, method);
        var knownSymbols = KnownSymbols.Create(ctx.SemanticModel.Compilation);

        return ProcessEndpointAttributes(ctx, method, returnTypeInfo, inferredErrors, knownSymbols);
    }


    private static EndpointData ProcessEndpointAttributes(
        GeneratorAttributeSyntaxContext ctx,
        IMethodSymbol method,
        in ErrorOrReturnTypeInfo returnTypeInfo,
        in EquatableArray<int> inferredErrors,
        KnownSymbols knownSymbols)
    {
        var descriptors = ImmutableArray.CreateBuilder<EndpointDescriptor>();
        var diagnostics = ImmutableArray.CreateBuilder<EndpointDiagnostic>();

        var (isObsolete, obsoleteMessage, isObsoleteError) = GetObsoleteInfo(method, knownSymbols);

        foreach (var attr in ctx.Attributes)
        {
            var descriptor = TryCreateEndpointDescriptor(attr, method, returnTypeInfo, isObsolete, obsoleteMessage,
                isObsoleteError, inferredErrors, diagnostics, knownSymbols);
            if (descriptor == null) continue;
            descriptors.Add(descriptor.Value);
        }

        return new EndpointData(
            new EquatableArray<EndpointDescriptor>(descriptors.ToImmutable()),
            new EquatableArray<EndpointDiagnostic>(diagnostics.ToImmutable()));
    }

    private static (bool IsObsolete, string? Message, bool IsError) GetObsoleteInfo(IMethodSymbol method,
        KnownSymbols symbols)
    {
        AttributeData? attr = null;
        foreach (var a in method.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(a.AttributeClass, symbols.Obsolete))
            {
                attr = a;
                break;
            }
        }

        if (attr is null) return (false, null, false);

        var message = attr.ConstructorArguments.Length > 0 ? attr.ConstructorArguments[0].Value as string : null;
        var isError = attr.ConstructorArguments.Length > 1 && (bool)attr.ConstructorArguments[1].Value!;

        return (true, message, isError);
    }

    private static EndpointDescriptor? TryCreateEndpointDescriptor(
        AttributeData attr,
        IMethodSymbol method,
        in ErrorOrReturnTypeInfo returnTypeInfo,
        bool isObsolete,
        string? obsoleteMessage,
        bool isObsoleteError,
        in EquatableArray<int> inferredErrors,
        ImmutableArray<EndpointDiagnostic>.Builder diagnostics,
        KnownSymbols knownSymbols)
    {
        if (!IsValidEndpointAttribute(attr, out var httpMethod, out var pattern))
            return null;

        var routeParameters = ExtractRouteParameters(pattern);
        var parameterResult = BindParameters(method, routeParameters, diagnostics, knownSymbols);

        if (!parameterResult.IsValid)
            return null;

        return new EndpointDescriptor(
            httpMethod,
            pattern,
            returnTypeInfo.SuccessTypeFqn!,
            returnTypeInfo.IsAsync,
            method.ContainingType.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat),
            method.Name,
            isObsolete,
            obsoleteMessage,
            isObsoleteError,
            new EquatableArray<EndpointParameter>(parameterResult.Parameters),
            inferredErrors,
            returnTypeInfo.IsSse,
            returnTypeInfo.SseItemTypeFqn,
            returnTypeInfo.UsesSseItem);
    }

    private static bool IsValidEndpointAttribute(
        AttributeData attr,
        out string httpMethod,
        out string pattern)
    {
        httpMethod = string.Empty;
        pattern = string.Empty;

        var attrClass = attr.AttributeClass?.ToDisplayString();
        if (attrClass is null)
            return false;

        // Check for base attribute [ErrorOrEndpoint("GET", "/path")]
        if (attrClass == WellKnownTypes.ErrorOrEndpointAttribute)
        {
            if (attr.ConstructorArguments.Length < 2)
                return false;

            if (attr.ConstructorArguments[0].Value is not string method ||
                attr.ConstructorArguments[1].Value is not string route ||
                string.IsNullOrWhiteSpace(method) ||
                string.IsNullOrWhiteSpace(route))
                return false;

            httpMethod = method;
            pattern = route;
            return true;
        }

        // Check for shorthand attributes [Get], [Post], etc.
        var shorthandMethod = attrClass switch
        {
            WellKnownTypes.GetAttribute => "GET",
            WellKnownTypes.PostAttribute => "POST",
            WellKnownTypes.PutAttribute => "PUT",
            WellKnownTypes.DeleteAttribute => "DELETE",
            WellKnownTypes.PatchAttribute => "PATCH",
            _ => null
        };

        if (shorthandMethod is null)
            return false;

        httpMethod = shorthandMethod;
        // Pattern comes from first constructor argument, defaults to "/"
        pattern = attr.ConstructorArguments.Length > 0 &&
                  attr.ConstructorArguments[0].Value is string p &&
                  !string.IsNullOrWhiteSpace(p)
            ? p
            : "/";
        return true;
    }



    /// <summary>
    ///     Result of extracting the ErrorOr return type, including SSE detection.
    /// </summary>
    private readonly record struct ErrorOrReturnTypeInfo(
        string? SuccessTypeFqn,
        bool IsAsync,
        bool IsSse,
        string? SseItemTypeFqn,
        bool UsesSseItem);

    private static ErrorOrReturnTypeInfo ExtractErrorOrReturnType(ITypeSymbol returnType)
    {
        var (unwrapped, isAsync) = UnwrapAsyncType(returnType);

        if (!IsErrorOrType(unwrapped, out var errorOrType))
            return new ErrorOrReturnTypeInfo(null, false, false, null, false);

        var innerType = errorOrType.TypeArguments[0];

        if (TryUnwrapAsyncEnumerable(innerType, out var elementType))
        {
            if (TryUnwrapSseItem(elementType, out var sseDataType))
            {
                var sseDataFqn = sseDataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var asyncEnumFqn = innerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return new ErrorOrReturnTypeInfo(asyncEnumFqn, isAsync, true, sseDataFqn, true);
            }
            else
            {
                var elementFqn = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var asyncEnumFqn = innerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return new ErrorOrReturnTypeInfo(asyncEnumFqn, isAsync, true, elementFqn, false);
            }
        }

        var successTypeFqn = innerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return new ErrorOrReturnTypeInfo(successTypeFqn, isAsync, false, null, false);
    }

    /// <summary>
    ///     Tries to unwrap IAsyncEnumerable&lt;T&gt; to get the element type.
    /// </summary>
    private static bool TryUnwrapAsyncEnumerable(ITypeSymbol type, out ITypeSymbol elementType)
    {
        elementType = null!;

        if (type is not INamedTypeSymbol { IsGenericType: true } named)
            return false;

        var constructedFrom = named.ConstructedFrom.ToDisplayString();
        if (constructedFrom != WellKnownTypes.IAsyncEnumerableT)
            return false;

        elementType = named.TypeArguments[0];
        return true;
    }

    /// <summary>
    ///     Tries to unwrap SseItem&lt;T&gt; to get the data type.
    /// </summary>
    private static bool TryUnwrapSseItem(ITypeSymbol type, out ITypeSymbol dataType)
    {
        dataType = null!;

        if (type is not INamedTypeSymbol { IsGenericType: true } named)
            return false;

        var constructedFrom = named.ConstructedFrom.ToDisplayString();
        if (constructedFrom != WellKnownTypes.SseItemT)
            return false;

        dataType = named.TypeArguments[0];
        return true;
    }

    private static (ITypeSymbol Type, bool IsAsync) UnwrapAsyncType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { IsGenericType: true } named)
            return (type, false);

        var constructedFrom = named.ConstructedFrom.ToDisplayString();

        return constructedFrom is WellKnownTypes.TaskT or
            WellKnownTypes.ValueTaskT
            ? (named.TypeArguments[0], true)
            : (type, false);
    }

    private static bool IsErrorOrType(ITypeSymbol type, out INamedTypeSymbol errorOrType)
    {
        errorOrType = null!;

        if (type is not INamedTypeSymbol { IsGenericType: true } named)
            return false;

        if (named.ConstructedFrom.ToDisplayString() != WellKnownTypes.ErrorOrT)
            return false;

        errorOrType = named;
        return true;
    }



    private static EquatableArray<int> InferErrorTypesFromMethod(GeneratorAttributeSyntaxContext ctx,
        IMethodSymbol method)
    {
        var body = GetMethodBody(method);
        if (body is null)
            return EquatableArray<int>.Empty;

        var errorTypes = CollectErrorTypes(ctx.SemanticModel, body);
        return ToSortedErrorArray(errorTypes);
    }

    private static SyntaxNode? GetMethodBody(IMethodSymbol method)
    {
        if (method.DeclaringSyntaxReferences.IsDefaultOrEmpty)
            return null;

        var syntax = method.DeclaringSyntaxReferences[0].GetSyntax();

        return syntax switch
        {
            MethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody,
            LocalFunctionStatementSyntax f => (SyntaxNode?)f.Body ?? f.ExpressionBody,
            _ => null
        };
    }

    private static HashSet<int> CollectErrorTypes(SemanticModel semanticModel, SyntaxNode body)
    {
        var set = new HashSet<int>();
        var visitedSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        CollectRecursive(body, visitedSymbols);

        return set;

        void CollectRecursive(SyntaxNode node, HashSet<ISymbol> visited)
        {
            foreach (var child in node.DescendantNodes())
            {
                if (IsErrorFactoryInvocation(child, out var factoryName))
                {
                    var errorType = MapErrorFactoryToType(factoryName);
                    if (errorType >= 0)
                        set.Add(errorType);
                    continue;
                }

                if (child is IdentifierNameSyntax or MemberAccessExpressionSyntax)
                {
                    var symbol = semanticModel.GetSymbolInfo(child).Symbol;
                    if (symbol is null || !visited.Add(symbol))
                        continue;

                    if (!SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly,
                            semanticModel.Compilation.Assembly))
                        continue;

                    if (symbol is IPropertySymbol or IFieldSymbol or ILocalSymbol or IMethodSymbol)
                    {
                        foreach (var reference in symbol.DeclaringSyntaxReferences)
                        {
                            var syntax = reference.GetSyntax();
                            var bodyToScan = syntax switch
                            {
                                PropertyDeclarationSyntax p => (SyntaxNode?)p.ExpressionBody ?? p.AccessorList,
                                MethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody,
                                VariableDeclaratorSyntax v => v.Initializer,
                                _ => syntax
                            };

                            if (bodyToScan != null)
                                CollectRecursive(bodyToScan, visited);
                        }
                    }
                }
            }
        }
    }

    private static bool IsErrorFactoryInvocation(SyntaxNode node, out string factoryName)
    {
        factoryName = string.Empty;

        if (node is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.Text: "Error" },
                    Name: IdentifierNameSyntax { Identifier.Text: var name }
                }
            })
            return false;

        factoryName = name;
        return true;
    }

    private static int MapErrorFactoryToType(string factoryName)
    {
        return factoryName switch
        {
            "Failure" => 0,
            "Unexpected" => 1,
            "Validation" => 2,
            "Conflict" => 3,
            "NotFound" => 4,
            "Unauthorized" => 5,
            "Forbidden" => 6,
            _ => -1
        };
    }

    private static EquatableArray<int> ToSortedErrorArray(HashSet<int> set)
    {
        if (set.Count == 0)
            return EquatableArray<int>.Empty;

        var array = set.ToArray();
        Array.Sort(array);
        return new EquatableArray<int>([.. array]);
    }



    private static readonly char[] s_routeSeparators = [':', '=', '?'];

    private static ImmutableHashSet<string> ExtractRouteParameters(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return ImmutableHashSet<string>.Empty;

        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        var span = pattern.AsSpan();

        for (var index = 0; index < span.Length;)
        {
            var (name, nextIndex) = TryExtractNextRouteParam(span, index);
            if (nextIndex < 0)
                break;

            if (name.Length > 0)
                builder.Add(name);

            index = nextIndex;
        }

        return builder.ToImmutable();
    }

#pragma warning disable EPS06
    private static (string Name, int NextIndex) TryExtractNextRouteParam(ReadOnlySpan<char> span, int index)
    {
        var start = span[index..].IndexOf('{');
        if (start < 0)
            return (string.Empty, -1);

        start += index;
        var end = span[(start + 1)..].IndexOf('}');
        if (end < 0)
            return (string.Empty, -1);

        end += start + 1;
        var token = span[(start + 1)..end];
        var name = token.IsEmpty ? string.Empty : ExtractRouteParameterName(token).ToString();

        return (name, end + 1);
    }

    private static ReadOnlySpan<char> ExtractRouteParameterName(ReadOnlySpan<char> token)
    {
        var name = token;
        var separatorIndex = name.IndexOfAny(s_routeSeparators);
        if (separatorIndex >= 0)
            name = name[..separatorIndex];

        return name.TrimStart('*');
    }
#pragma warning restore EPS06



    private static RoutePrimitiveKind? TryGetRoutePrimitiveKind(ITypeSymbol type)
    {
        return type.SpecialType switch
        {
            SpecialType.System_String => RoutePrimitiveKind.String,
            SpecialType.System_Int32 => RoutePrimitiveKind.Int32,
            SpecialType.System_Int64 => RoutePrimitiveKind.Int64,
            SpecialType.System_Int16 => RoutePrimitiveKind.Int16,
            SpecialType.System_UInt32 => RoutePrimitiveKind.UInt32,
            SpecialType.System_UInt64 => RoutePrimitiveKind.UInt64,
            SpecialType.System_UInt16 => RoutePrimitiveKind.UInt16,
            SpecialType.System_Byte => RoutePrimitiveKind.Byte,
            SpecialType.System_SByte => RoutePrimitiveKind.SByte,
            SpecialType.System_Boolean => RoutePrimitiveKind.Boolean,
            SpecialType.System_Decimal => RoutePrimitiveKind.Decimal,
            SpecialType.System_Double => RoutePrimitiveKind.Double,
            SpecialType.System_Single => RoutePrimitiveKind.Single,
            _ => TryGetRoutePrimitiveKindByFqn(type)
        };
    }

    private static RoutePrimitiveKind? TryGetRoutePrimitiveKindByFqn(ITypeSymbol type)
    {
        var fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return fqn switch
        {
            WellKnownTypes.Fqn.Guid => RoutePrimitiveKind.Guid,
            WellKnownTypes.Fqn.DateTime => RoutePrimitiveKind.DateTime,
            WellKnownTypes.Fqn.DateTimeOffset => RoutePrimitiveKind.DateTimeOffset,
            WellKnownTypes.Fqn.DateOnly => RoutePrimitiveKind.DateOnly,
            WellKnownTypes.Fqn.TimeOnly => RoutePrimitiveKind.TimeOnly,
            WellKnownTypes.Fqn.TimeSpan => RoutePrimitiveKind.TimeSpan,
            _ => null
        };
    }



    /// <summary>
    ///     Detects custom binding methods on a type:
    ///     - IBindableFromHttpContext&lt;T&gt; interface
    ///     - static BindAsync(HttpContext) or BindAsync(HttpContext, ParameterInfo)
    ///     - static TryParse(string, out T) or TryParse(string, IFormatProvider, out T)
    /// </summary>
    private static CustomBindingMethod DetectCustomBinding(ITypeSymbol type, KnownSymbols knownSymbols)
    {
        if (type is not INamedTypeSymbol namedType)
            return CustomBindingMethod.None;

        if (IsPrimitiveOrWellKnownType(namedType))
            return CustomBindingMethod.None;

        if (ImplementsBindableInterface(namedType, knownSymbols))
            return CustomBindingMethod.Bindable;

        var bindAsyncMethod = DetectBindAsyncMethod(namedType, knownSymbols);
        if (bindAsyncMethod != CustomBindingMethod.None)
            return bindAsyncMethod;

        return DetectTryParseMethod(namedType);
    }

    /// <summary>
    ///     Checks if type is a primitive or well-known type that has built-in binding support.
    ///     These types should NOT use custom TryParse binding even though they have TryParse methods.
    /// </summary>
    private static bool IsPrimitiveOrWellKnownType(INamedTypeSymbol type)
    {
        if (type.SpecialType is not SpecialType.None)
            return true;

        var fqn = type.ToDisplayString();
        return fqn is
            "System.Guid" or "global::System.Guid" or
            "System.DateTime" or "global::System.DateTime" or
            "System.DateTimeOffset" or "global::System.DateTimeOffset" or
            "System.DateOnly" or "global::System.DateOnly" or
            "System.TimeOnly" or "global::System.TimeOnly" or
            "System.TimeSpan" or "global::System.TimeSpan";
    }

    private static bool ImplementsBindableInterface(INamedTypeSymbol type, KnownSymbols knownSymbols)
    {
        if (knownSymbols.IBindableFromHttpContext is not null)
        {
            foreach (var iface in type.AllInterfaces)
            {
                if (iface.IsGenericType &&
                    SymbolEqualityComparer.Default.Equals(iface.ConstructedFrom, knownSymbols.IBindableFromHttpContext))
                    return true;
            }
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (iface.IsGenericType)
            {
                var fqn = iface.ConstructedFrom.ToDisplayString();
                if (fqn == "Microsoft.AspNetCore.Http.IBindableFromHttpContext<TSelf>")
                    return true;
            }
        }

        return false;
    }

    private static CustomBindingMethod DetectBindAsyncMethod(INamedTypeSymbol type, KnownSymbols knownSymbols)
    {
        foreach (var member in type.GetMembers("BindAsync"))
        {
            if (member is not IMethodSymbol { IsStatic: true, ReturnsVoid: false } method)
                continue;

            var returnFqn = method.ReturnType.ToDisplayString();
            if (!returnFqn.StartsWith("System.Threading.Tasks.ValueTask<") &&
                !returnFqn.StartsWith("System.Threading.Tasks.Task<"))
                continue;

            if (method.Parameters.Length < 1)
                continue;

            var firstParam = method.Parameters[0].Type;
            var firstParamFqn = firstParam.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (firstParamFqn != WellKnownTypes.Fqn.HttpContext &&
                !IsHttpContextType(firstParam, knownSymbols))
                continue;

            if (method.Parameters.Length >= 2)
            {
                var secondParam = method.Parameters[1].Type;
                var secondParamFqn = secondParam.ToDisplayString();
                if (secondParamFqn.EndsWith("ParameterInfo") ||
                    (knownSymbols.ParameterInfo is not null &&
                     SymbolEqualityComparer.Default.Equals(secondParam, knownSymbols.ParameterInfo)))
                    return CustomBindingMethod.BindAsyncWithParam;
            }

            return CustomBindingMethod.BindAsync;
        }

        return CustomBindingMethod.None;
    }

    private static bool IsHttpContextType(ITypeSymbol type, KnownSymbols knownSymbols)
    {
        if (knownSymbols.HttpContextSymbol is not null &&
            SymbolEqualityComparer.Default.Equals(type, knownSymbols.HttpContextSymbol))
            return true;

        var fqn = type.ToDisplayString();
        return fqn is "Microsoft.AspNetCore.Http.HttpContext" or "HttpContext";
    }

    private static CustomBindingMethod DetectTryParseMethod(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers("TryParse"))
        {
            if (member is not IMethodSymbol { IsStatic: true } method)
                continue;

            if (method.ReturnType.SpecialType != SpecialType.System_Boolean)
                continue;

            if (method.Parameters.Length < 2)
                continue;

            if (method.Parameters[0].Type.SpecialType != SpecialType.System_String)
                continue;

            var lastParam = method.Parameters[^1];
            if (lastParam.RefKind != RefKind.Out)
                continue;

            if (method.Parameters.Length == 3)
            {
                var middleParam = method.Parameters[1].Type;
                var middleFqn = middleParam.ToDisplayString();
                if (middleFqn.Contains("IFormatProvider"))
                    return CustomBindingMethod.TryParseWithFormat;
            }

            if (method.Parameters.Length == 2)
                return CustomBindingMethod.TryParse;
        }

        return CustomBindingMethod.None;
    }



    private static ParameterBindingResult BindParameters(
        IMethodSymbol method,
        ImmutableHashSet<string> routeParameters,
        ImmutableArray<EndpointDiagnostic>.Builder diagnostics,
        KnownSymbols knownSymbols)
    {
        if (method.Parameters.Length is 0) return ParameterBindingResult.Empty;

        var metas = BuildParameterMetas(method.Parameters, knownSymbols, diagnostics);

        if (metas.Count(static m => m.HasFromBody) > 1)
        {
            diagnostics.Add(
                EndpointDiagnostic.Create(DiagnosticDescriptors.MultipleBodyParameters, method, method.Name));
            return ParameterBindingResult.Invalid;
        }

        var hasBody = metas.Any(static m => m.HasFromBody);
        var hasForm = metas.Any(static m => m.HasFromForm || m.IsFormFile || m.IsFormFileCollection || m.IsFormCollection);
        var hasStream = metas.Any(static m => m.IsStream || m.IsPipeReader);

        var bodySourceCount = (hasBody ? 1 : 0) + (hasForm ? 1 : 0) + (hasStream ? 1 : 0);
        if (bodySourceCount > 1)
        {
            diagnostics.Add(
                EndpointDiagnostic.Create(DiagnosticDescriptors.MultipleBodySources, method, method.Name));
            return ParameterBindingResult.Invalid;
        }

        var fromFormDtoCount = metas.Count(static m =>
            m.HasFromForm &&
            m.RouteKind is null && // Not a primitive
            !(m.IsCollection && m.CollectionItemPrimitiveKind is not null) && // Not a collection of primitives
            !m.IsFormFile &&
            !m.IsFormFileCollection);

        if (fromFormDtoCount > 1)
        {
            diagnostics.Add(
                EndpointDiagnostic.Create(DiagnosticDescriptors.MultipleFromFormParameters, method, method.Name));
            return ParameterBindingResult.Invalid;
        }

        // EOE010: Info diagnostic when form binding is used (reminder about Content-Type)
        if (hasForm)
        {
            diagnostics.Add(
                EndpointDiagnostic.Create(DiagnosticDescriptors.FormContentTypeRequired, method, method.Name));
        }

        return BuildEndpointParameters(metas, routeParameters, method, diagnostics, knownSymbols);
    }

    private static ParameterMeta[] BuildParameterMetas(
        ImmutableArray<IParameterSymbol> parameters,
        KnownSymbols knownSymbols,
        ImmutableArray<EndpointDiagnostic>.Builder diagnostics)
    {
        var metas = new ParameterMeta[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
            metas[i] = CreateParameterMeta(i, parameters[i], knownSymbols, diagnostics);
        return metas;
    }

    private static ParameterMeta CreateParameterMeta(
        int index,
        IParameterSymbol parameter,
        KnownSymbols knownSymbols,
        ImmutableArray<EndpointDiagnostic>.Builder _)
    {
        var type = parameter.Type;
        var typeFqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var hasFromRoute = HasParameterAttribute(parameter, knownSymbols.FromRoute, WellKnownTypes.FromRouteAttribute);
        var hasFromQuery = HasParameterAttribute(parameter, knownSymbols.FromQuery, WellKnownTypes.FromQueryAttribute);
        var hasFromHeader =
            HasParameterAttribute(parameter, knownSymbols.FromHeader, WellKnownTypes.FromHeaderAttribute);
        var hasFromKeyedServices =
            HasParameterAttribute(parameter, knownSymbols.FromKeyedServices, WellKnownTypes.FromKeyedServicesAttribute);
        var hasAsParameters =
            HasParameterAttribute(parameter, knownSymbols.AsParameters, WellKnownTypes.AsParametersAttribute);
        var hasFromForm = HasParameterAttribute(parameter, knownSymbols.FromForm, WellKnownTypes.FromFormAttribute);

        var routeName = hasFromRoute ? TryGetFromRouteName(parameter, knownSymbols) ?? parameter.Name : parameter.Name;
        var queryName = hasFromQuery ? TryGetFromQueryName(parameter, knownSymbols) ?? parameter.Name : parameter.Name;
        var headerName =
            hasFromHeader ? TryGetFromHeaderName(parameter, knownSymbols) ?? parameter.Name : parameter.Name;
        var formName = hasFromForm ? TryGetFromFormName(parameter, knownSymbols) ?? parameter.Name : parameter.Name;
        var keyedServiceKey =
            hasFromKeyedServices ? ExtractKeyFromKeyedServiceAttribute(parameter, knownSymbols) : null;

        var (isNullable, isNonNullableValueType) = GetParameterNullability(type, parameter.NullableAnnotation);
        var (isCollection, itemType, itemPrimitiveKind) = AnalyzeCollectionType(type);

        var isFormFile = IsFormFileType(type, typeFqn, knownSymbols);
        var isFormFileCollection = IsFormFileCollectionType(type, typeFqn, knownSymbols);
        var isFormCollection = IsFormCollectionType(type, typeFqn, knownSymbols);

        var isStream = IsStreamType(typeFqn);
        var isPipeReader = IsPipeReaderType(typeFqn);

        return new ParameterMeta(
            index, parameter, parameter.Name, typeFqn, TryGetRoutePrimitiveKind(type),
            HasParameterAttribute(parameter, knownSymbols.FromServices, WellKnownTypes.FromServicesAttribute),
            hasFromKeyedServices, keyedServiceKey,
            HasParameterAttribute(parameter, knownSymbols.FromBody, WellKnownTypes.FromBodyAttribute),
            hasFromRoute, hasFromQuery, hasFromHeader, hasAsParameters,
            routeName, queryName, headerName,
            typeFqn == WellKnownTypes.Fqn.CancellationToken,
            typeFqn == WellKnownTypes.Fqn.HttpContext,
            isNullable, isNonNullableValueType,
            isCollection, itemType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), itemPrimitiveKind,
            hasFromForm, formName, isFormFile, isFormFileCollection, isFormCollection,
            isStream, isPipeReader,
            DetectCustomBinding(type, knownSymbols));
    }

    private static bool IsFormFileType(ITypeSymbol type, string typeFqn, KnownSymbols knownSymbols)
    {
        if (knownSymbols.IFormFile is not null &&
            SymbolEqualityComparer.Default.Equals(type, knownSymbols.IFormFile))
            return true;

        return typeFqn is WellKnownTypes.Fqn.IFormFile
            or WellKnownTypes.IFormFile
            or "IFormFile";
    }

    private static bool IsFormFileCollectionType(ITypeSymbol type, string typeFqn, KnownSymbols knownSymbols)
    {
        if (knownSymbols.IFormFileCollection is not null &&
            SymbolEqualityComparer.Default.Equals(type, knownSymbols.IFormFileCollection))
            return true;

        if (typeFqn is WellKnownTypes.Fqn.IFormFileCollection
            or WellKnownTypes.IFormFileCollection
            or "IFormFileCollection")
            return true;

        return IsFormFileReadOnlyList(type, knownSymbols);
    }

    private static bool IsFormFileReadOnlyList(ITypeSymbol type, KnownSymbols knownSymbols)
    {
        if (type is not INamedTypeSymbol { IsGenericType: true } named)
            return false;

        var origin = named.ConstructedFrom.ToDisplayString();
        if (origin != "System.Collections.Generic.IReadOnlyList<T>")
            return false;

        var itemType = named.TypeArguments[0];
        var itemFqn = itemType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return IsFormFileType(itemType, itemFqn, knownSymbols);
    }

    private static bool IsFormCollectionType(ISymbol type, string typeFqn, KnownSymbols knownSymbols)
    {
        if (knownSymbols.IFormCollection is not null &&
            SymbolEqualityComparer.Default.Equals(type, knownSymbols.IFormCollection))
            return true;

        return typeFqn is WellKnownTypes.Fqn.IFormCollection
            or "Microsoft.AspNetCore.Http.IFormCollection"
            or "IFormCollection";
    }

    private static bool IsStreamType(string typeFqn)
    {
        return typeFqn is WellKnownTypes.Fqn.Stream
            or WellKnownTypes.Stream
            or "Stream"
            or "System.IO.Stream";
    }

    private static bool IsPipeReaderType(string typeFqn)
    {
        return typeFqn is WellKnownTypes.Fqn.PipeReader
            or WellKnownTypes.PipeReader
            or "PipeReader"
            or "System.IO.Pipelines.PipeReader";
    }

    private static (bool IsCollection, ITypeSymbol? ItemType, RoutePrimitiveKind? Kind) AnalyzeCollectionType(
        ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String) return (false, null, null);

        ITypeSymbol? itemType = null;
        if (type is IArrayTypeSymbol arrayType)
            itemType = arrayType.ElementType;
        else if (type is INamedTypeSymbol { IsGenericType: true } named)
        {
            var origin = named.ConstructedFrom.ToDisplayString();
            if (origin.StartsWith("System.Collections.Generic.List") ||
                origin.StartsWith("System.Collections.Generic.IList") ||
                origin.StartsWith("System.Collections.Generic.IEnumerable") ||
                origin.StartsWith("System.Collections.Generic.IReadOnlyList"))
                itemType = named.TypeArguments[0];
        }

        if (itemType is not null) return (true, itemType, TryGetRoutePrimitiveKind(itemType));

        return (false, null, null);
    }

    private static string? ExtractKeyFromKeyedServiceAttribute(IParameterSymbol parameter, KnownSymbols knownSymbols)
    {
        AttributeData? attr = null;
        foreach (var a in parameter.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(a.AttributeClass, knownSymbols.FromKeyedServices))
            {
                attr = a;
                break;
            }
        }

        if (attr is null || attr.ConstructorArguments.Length == 0) return null;

        var val = attr.ConstructorArguments[0].Value;
        return val switch { string s => $"\"{s}\"", _ => val?.ToString() };
    }

    private static bool HasParameterAttribute(IParameterSymbol parameter, INamedTypeSymbol? attributeSymbol,
        string attributeName)
    {
        var attributes = parameter.GetAttributes();

        if (attributeSymbol is not null)
        {
            foreach (var attr in attributes)
            {
                if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attributeSymbol))
                    return true;
            }
        }

        var shortName = attributeName[(attributeName.LastIndexOf('.') + 1)..];
        var shortNameWithoutAttr = shortName.EndsWith("Attribute")
            ? shortName[..^"Attribute".Length]
            : shortName;

        foreach (var attr in attributes)
        {
            var display = attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (display is null) continue;

            if (display.StartsWith("global::"))
                display = display[8..];

            if (display == attributeName ||
                display.EndsWith($".{shortName}") ||
                display == shortName ||
                display == shortNameWithoutAttr)
                return true;
        }

        return false;
    }

    private static string? TryGetFromRouteName(IParameterSymbol parameter, KnownSymbols knownSymbols)
    {
        return TryGetAttributeName(parameter, knownSymbols.FromRoute, WellKnownTypes.FromRouteAttribute);
    }

    private static string? TryGetFromQueryName(IParameterSymbol parameter, KnownSymbols knownSymbols)
    {
        return TryGetAttributeName(parameter, knownSymbols.FromQuery, WellKnownTypes.FromQueryAttribute);
    }

    private static string? TryGetFromHeaderName(IParameterSymbol parameter, KnownSymbols knownSymbols)
    {
        return TryGetAttributeName(parameter, knownSymbols.FromHeader, WellKnownTypes.FromHeaderAttribute);
    }

    private static string? TryGetFromFormName(IParameterSymbol parameter, KnownSymbols knownSymbols)
    {
        return TryGetAttributeName(parameter, knownSymbols.FromForm, WellKnownTypes.FromFormAttribute);
    }

    private static string? TryGetAttributeName(
        IParameterSymbol parameter,
        INamedTypeSymbol? attributeSymbol,
        string attributeName)
    {
        var attributes = parameter.GetAttributes();

        if (attributeSymbol is not null)
        {
            foreach (var attr in attributes)
            {
                if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attributeSymbol))
                    return ExtractNameFromAttribute(attr);
            }
        }

        var shortName = attributeName[(attributeName.LastIndexOf('.') + 1)..];
        var shortNameWithoutAttr = shortName.EndsWith("Attribute")
            ? shortName[..^"Attribute".Length]
            : shortName;

        foreach (var attr in attributes)
        {
            var display = attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (display is null) continue;

            if (display.StartsWith("global::"))
                display = display[8..];

            if (display == attributeName ||
                display.EndsWith($".{shortName}") ||
                display == shortName ||
                display == shortNameWithoutAttr)
                return ExtractNameFromAttribute(attr);
        }

        return null;
    }

    private static string? ExtractNameFromAttribute(AttributeData? attr)
    {
        if (attr is null) return null;

        foreach (var namedArg in attr.NamedArguments)
        {
            if (string.Equals(namedArg.Key, "Name", StringComparison.OrdinalIgnoreCase) &&
                namedArg.Value.Value is string name && !string.IsNullOrWhiteSpace(name))
                return name;
        }

        if (attr.ConstructorArguments.Length > 0 &&
            attr.ConstructorArguments[0].Value is string ctorArg &&
            !string.IsNullOrWhiteSpace(ctorArg))
            return ctorArg;

        if (attr.ApplicationSyntaxReference?.GetSyntax() is { } syntax)
        {
            var syntaxText = syntax.ToString();
            var nameMatch = Regex.Match(
                syntaxText,
                @"Name\s*=\s*""([^""]+)""",
                RegexOptions.IgnoreCase);
            if (nameMatch.Success)
                return nameMatch.Groups[1].Value;
        }

        return null;
    }

    private static (bool IsNullable, bool IsNonNullableValueType) GetParameterNullability(
        ITypeSymbol type,
        NullableAnnotation annotation)
    {
        if (type.IsReferenceType)
            return (annotation == NullableAnnotation.Annotated, false);

        if (type is INamedTypeSymbol { IsGenericType: true } named &&
            named.ConstructedFrom.ToDisplayString() == "System.Nullable<T>")
            return (true, false);

        return (false, true);
    }


    private static ParameterBindingResult BuildEndpointParameters(
        ParameterMeta[] metas,
        ImmutableHashSet<string> routeParameters,
        IMethodSymbol method,
        ImmutableArray<EndpointDiagnostic>.Builder diagnostics,
        KnownSymbols knownSymbols)
    {
        var builder = ImmutableArray.CreateBuilder<EndpointParameter>(metas.Length);
        var isValid = true;

        foreach (var meta in metas)
        {
            var result = ClassifyParameter(in meta, routeParameters, method, diagnostics, knownSymbols);
            if (result.IsError)
            {
                isValid = false;
                continue;
            }

            builder.Add(result.Parameter);
        }

        return isValid
            ? new ParameterBindingResult(true, builder.ToImmutable())
            : ParameterBindingResult.Invalid;
    }

    private static ParameterClassificationResult ClassifyParameter(
        in ParameterMeta meta,
        ImmutableHashSet<string> routeParameters,
        IMethodSymbol method,
        ImmutableArray<EndpointDiagnostic>.Builder diagnostics,
        KnownSymbols knownSymbols)
    {
        if (meta.HasAsParameters)
            return ClassifyAsParameters(in meta, routeParameters, method, diagnostics, knownSymbols);
        if (meta.HasFromBody) return ParameterSuccess(in meta, EndpointParameterSource.Body);

        if (meta.HasFromForm)
            return ClassifyFromFormParameter(in meta, method, diagnostics, knownSymbols);

        if (meta.HasFromServices) return ParameterSuccess(in meta, EndpointParameterSource.Service);
        if (meta.HasFromKeyedServices)
        {
            return ParameterSuccess(in meta, EndpointParameterSource.KeyedService,
                keyedServiceKey: meta.KeyedServiceKey);
        }

        if (meta.HasFromHeader)
            return ParameterSuccess(in meta, EndpointParameterSource.Header, headerName: meta.HeaderName);
        if (meta.HasFromRoute) return ClassifyFromRouteParameter(in meta, routeParameters, method, diagnostics);
        if (meta.HasFromQuery) return ClassifyFromQueryParameter(in meta, method, diagnostics);

        if (meta.IsHttpContext) return ParameterSuccess(in meta, EndpointParameterSource.HttpContext);
        if (meta.IsCancellationToken) return ParameterSuccess(in meta, EndpointParameterSource.CancellationToken);

        if (meta.IsFormFile)
        {
            // EOE009: Warn if IFormFile is non-nullable (files are inherently optional)
            if (!meta.IsNullable)
            {
                diagnostics.Add(EndpointDiagnostic.Create(
                    DiagnosticDescriptors.FormFileNotNullable, meta.Symbol, meta.Name, method.Name));
            }
            return ParameterSuccess(in meta, EndpointParameterSource.FormFile, formName: meta.Name);
        }
        if (meta.IsFormFileCollection)
            return ParameterSuccess(in meta, EndpointParameterSource.FormFiles, formName: meta.Name);

        if (meta.IsFormCollection)
        {
            // IFormCollection requires explicit [FromForm] - it does NOT auto-bind like IFormFile
            if (!meta.HasFromForm)
            {
                diagnostics.Add(EndpointDiagnostic.Create(
                    DiagnosticDescriptors.FormCollectionRequiresAttribute, meta.Symbol, meta.Name, method.Name));
                return ParameterClassificationResult.Error;
            }
            // With [FromForm], IFormCollection binds to the raw form data
            return ParameterSuccess(in meta, EndpointParameterSource.FormCollection, formName: meta.FormName);
        }

        if (meta.IsStream)
            return ParameterSuccess(in meta, EndpointParameterSource.Stream);
        if (meta.IsPipeReader)
            return ParameterSuccess(in meta, EndpointParameterSource.PipeReader);

        if (routeParameters.Contains(meta.Name)) return ClassifyImplicitRouteParameter(in meta, method, diagnostics);

        if (meta.RouteKind is not null || (meta.IsCollection && meta.CollectionItemPrimitiveKind is not null))
            return ParameterSuccess(in meta, EndpointParameterSource.Query, queryName: meta.Name);

        if (meta.CustomBinding != CustomBindingMethod.None)
        {
            if (meta.CustomBinding is CustomBindingMethod.TryParse or CustomBindingMethod.TryParseWithFormat)
            {
                var source = routeParameters.Contains(meta.Name)
                    ? EndpointParameterSource.Route
                    : EndpointParameterSource.Query;
                return ParameterSuccess(in meta, source,
                    source == EndpointParameterSource.Route ? meta.Name : null,
                    queryName: source == EndpointParameterSource.Query ? meta.Name : null,
                    customBinding: meta.CustomBinding);
            }

            return ParameterSuccess(in meta, EndpointParameterSource.Query, // Query as placeholder
                queryName: meta.Name,
                customBinding: meta.CustomBinding);
        }

        diagnostics.Add(EndpointDiagnostic.Create(DiagnosticDescriptors.AmbiguousParameter, meta.Symbol, meta.Name,
            method.Name));
        return ParameterClassificationResult.Error;
    }

    private static ParameterClassificationResult ClassifyAsParameters(
        in ParameterMeta meta,
        ImmutableHashSet<string> routeParameters,
        IMethodSymbol method,
        ImmutableArray<EndpointDiagnostic>.Builder diagnostics,
        KnownSymbols knownSymbols)
    {
        if (meta.Symbol.Type is not INamedTypeSymbol typeSymbol)
        {
            return EmitParameterError(in meta, method, diagnostics,
                "[AsParameters] can only be used on class or struct types.");
        }

        var constructor = typeSymbol.Constructors
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault(); // Simplified selection: greedily pick longest constructor. Ideally match C# primary ctor logic.

        if (constructor is null)
            return EmitParameterError(in meta, method, diagnostics, "[AsParameters] type must have a constructor.");

        var children = ImmutableArray.CreateBuilder<EndpointParameter>();
        for (var i = 0; i < constructor.Parameters.Length; i++)
        {
            var paramSymbol = constructor.Parameters[i];
            var childMeta =
                CreateParameterMeta(i, paramSymbol, knownSymbols, diagnostics); // Reusing CreateParameterMeta logic

            var result = ClassifyParameter(in childMeta, routeParameters, method, diagnostics, knownSymbols);

            if (result.IsError)
                return ParameterClassificationResult.Error; // Propagate error

            children.Add(result.Parameter);
        }

        return new ParameterClassificationResult(false, new EndpointParameter(
            meta.Name,
            meta.TypeFqn,
            EndpointParameterSource.AsParameters,
            null,
            meta.IsNullable,
            meta.IsNonNullableValueType,
            false,
            null,
            new EquatableArray<EndpointParameter>(children.ToImmutable())));
    }

    private static ParameterClassificationResult ClassifyFromQueryParameter(
        in ParameterMeta meta,
        IMethodSymbol method,
        ImmutableArray<EndpointDiagnostic>.Builder diagnostics)
    {
        if (meta.RouteKind is not null || (meta.IsCollection && meta.CollectionItemPrimitiveKind is not null))
            return ParameterSuccess(in meta, EndpointParameterSource.Query, queryName: meta.QueryName);

        return EmitParameterError(in meta, method, diagnostics,
            "[FromQuery] only supports primitives or collections of primitives (AOT limitation)");
    }

    private static ParameterClassificationResult ClassifyFromFormParameter(
        in ParameterMeta meta,
        IMethodSymbol method,
        ImmutableArray<EndpointDiagnostic>.Builder diagnostics,
        KnownSymbols knownSymbols)
    {
        if (meta.IsFormFile)
        {
            // EOE009: Warn if IFormFile is non-nullable (files are inherently optional)
            if (!meta.IsNullable)
            {
                diagnostics.Add(EndpointDiagnostic.Create(
                    DiagnosticDescriptors.FormFileNotNullable, meta.Symbol, meta.Name, method.Name));
            }
            return ParameterSuccess(in meta, EndpointParameterSource.FormFile, formName: meta.FormName);
        }

        if (meta.IsFormFileCollection)
            return ParameterSuccess(in meta, EndpointParameterSource.FormFiles, formName: meta.FormName);

        if (meta.RouteKind is not null)
            return ParameterSuccess(in meta, EndpointParameterSource.Form, formName: meta.FormName);

        if (meta.IsCollection && meta.CollectionItemPrimitiveKind is not null)
            return ParameterSuccess(in meta, EndpointParameterSource.Form, formName: meta.FormName);

        return ClassifyFormDtoParameter(in meta, method, diagnostics, knownSymbols);
    }

    private static ParameterClassificationResult ClassifyFormDtoParameter(
        in ParameterMeta meta,
        IMethodSymbol method,
        ImmutableArray<EndpointDiagnostic>.Builder diagnostics,
        KnownSymbols knownSymbols)
    {
        if (meta.Symbol.Type is not INamedTypeSymbol typeSymbol)
        {
            diagnostics.Add(EndpointDiagnostic.Create(
                DiagnosticDescriptors.UnsupportedFormDtoShape, meta.Symbol, meta.Name, method.Name,
                "[FromForm] DTO must be a class or struct type"));
            return ParameterClassificationResult.Error;
        }

        var constructor = typeSymbol.Constructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public && !c.IsStatic)
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();

        if (constructor is null)
        {
            diagnostics.Add(EndpointDiagnostic.Create(
                DiagnosticDescriptors.UnsupportedFormDtoShape, meta.Symbol, meta.Name, method.Name,
                "[FromForm] DTO must have a public constructor"));
            return ParameterClassificationResult.Error;
        }

        var children = ImmutableArray.CreateBuilder<EndpointParameter>();
        foreach (var paramSymbol in constructor.Parameters)
        {
            var childMeta = CreateParameterMeta(children.Count, paramSymbol, knownSymbols, diagnostics);
            var childFqn = childMeta.TypeFqn;

            if (childMeta.RouteKind is not null ||
                (childMeta.IsCollection && childMeta.CollectionItemPrimitiveKind is not null) ||
                childMeta.IsFormFile ||
                childMeta.IsFormFileCollection)
            {
                EndpointParameterSource childSource;
                if (childMeta.IsFormFile)
                    childSource = EndpointParameterSource.FormFile;
                else if (childMeta.IsFormFileCollection)
                    childSource = EndpointParameterSource.FormFiles;
                else
                    childSource = EndpointParameterSource.Form;

                children.Add(new EndpointParameter(
                    childMeta.Name,
                    childMeta.TypeFqn,
                    childSource,
                    childMeta.FormName,
                    childMeta.IsNullable,
                    childMeta.IsNonNullableValueType,
                    childMeta.IsCollection,
                    childMeta.CollectionItemTypeFqn,
                    EquatableArray<EndpointParameter>.Empty));
                continue;
            }

            diagnostics.Add(EndpointDiagnostic.Create(
                DiagnosticDescriptors.UnsupportedFormDtoShape, meta.Symbol, meta.Name, method.Name,
                $"[FromForm] DTO property '{childMeta.Name}' has unsupported type '{childFqn}'. Only primitives, collections of primitives, IFormFile, and IFormFileCollection are allowed."));
            return ParameterClassificationResult.Error;
        }

        return new ParameterClassificationResult(false, new EndpointParameter(
            meta.Name,
            meta.TypeFqn,
            EndpointParameterSource.Form,
            meta.FormName,
            meta.IsNullable,
            meta.IsNonNullableValueType,
            false,
            null,
            new EquatableArray<EndpointParameter>(children.ToImmutable())));
    }

    private static ParameterClassificationResult EmitParameterError(
        in ParameterMeta meta,
        IMethodSymbol method,
        ImmutableArray<EndpointDiagnostic>.Builder diagnostics,
        string reason)
    {
        diagnostics.Add(EndpointDiagnostic.Create(
            DiagnosticDescriptors.UnsupportedParameter, meta.Symbol, meta.Name, method.Name, reason));
        return ParameterClassificationResult.Error;
    }

    private static ParameterClassificationResult ParameterSuccess(
        in ParameterMeta meta,
        EndpointParameterSource source,
        string? routeName = null,
        string? headerName = null,
        string? queryName = null,
        string? keyedServiceKey = null,
        string? formName = null,
        CustomBindingMethod customBinding = CustomBindingMethod.None)
    {
        return new ParameterClassificationResult(false, new EndpointParameter(
            meta.Name,
            meta.TypeFqn,
            source,
            routeName ?? queryName ?? headerName ?? keyedServiceKey ?? formName,
            meta.IsNullable,
            meta.IsNonNullableValueType,
            meta.IsCollection,
            meta.CollectionItemTypeFqn,
            EquatableArray<EndpointParameter>.Empty,
            customBinding));
    }

    private static ParameterClassificationResult ClassifyFromRouteParameter(
        in ParameterMeta meta,
        ImmutableHashSet<string> routeParameters,
        IMethodSymbol method,
        ImmutableArray<EndpointDiagnostic>.Builder diagnostics)
    {
        var hasTryParse = meta.CustomBinding is CustomBindingMethod.TryParse
            or CustomBindingMethod.TryParseWithFormat;

        if (meta.RouteKind is null && !hasTryParse)
        {
            return EmitParameterError(in meta, method, diagnostics,
                "[FromRoute] requires a supported route primitive type or a type with TryParse");
        }

        if (!routeParameters.Contains(meta.RouteName))
        {
            return EmitParameterError(in meta, method, diagnostics,
                $"route parameter '{meta.RouteName}' not found in pattern");
        }

        return ParameterSuccess(in meta, EndpointParameterSource.Route, meta.RouteName,
            customBinding: meta.CustomBinding);
    }

    private static ParameterClassificationResult ClassifyImplicitRouteParameter(
        in ParameterMeta meta,
        IMethodSymbol method,
        ImmutableArray<EndpointDiagnostic>.Builder diagnostics)
    {
        var hasTryParse = meta.CustomBinding is CustomBindingMethod.TryParse
            or CustomBindingMethod.TryParseWithFormat;

        if (meta.RouteKind is null && !hasTryParse)
        {
            return EmitParameterError(in meta, method, diagnostics,
                "route parameters must use supported primitive types or a type with TryParse");
        }

        return ParameterSuccess(in meta, EndpointParameterSource.Route, meta.Name,
            customBinding: meta.CustomBinding);
    }


    private readonly record struct ParameterClassificationResult(bool IsError, EndpointParameter Parameter)
    {
        public static readonly ParameterClassificationResult Error = new(true, default);
    }

}

internal readonly record struct ParameterBindingResult(bool IsValid, ImmutableArray<EndpointParameter> Parameters)
{
    public static readonly ParameterBindingResult Empty = new(true, ImmutableArray<EndpointParameter>.Empty);
    public static readonly ParameterBindingResult Invalid = new(false, ImmutableArray<EndpointParameter>.Empty);
}

internal sealed record KnownSymbols(
    INamedTypeSymbol? FromBody,
    INamedTypeSymbol? FromServices,
    INamedTypeSymbol? FromKeyedServices,
    INamedTypeSymbol? FromRoute,
    INamedTypeSymbol? FromQuery,
    INamedTypeSymbol? FromHeader,
    INamedTypeSymbol? AsParameters,
    INamedTypeSymbol? Obsolete,
    INamedTypeSymbol? FromForm,
    INamedTypeSymbol? IFormFile,
    INamedTypeSymbol? IFormFileCollection,
    INamedTypeSymbol? IFormCollection,
    INamedTypeSymbol? IBindableFromHttpContext,
    INamedTypeSymbol? HttpContextSymbol,
    INamedTypeSymbol? ParameterInfo)
{
    public static KnownSymbols Create(Compilation compilation)
    {
        return new KnownSymbols(
            compilation.GetTypeByMetadataName(WellKnownTypes.FromBodyAttribute),
            compilation.GetTypeByMetadataName(WellKnownTypes.FromServicesAttribute),
            compilation.GetTypeByMetadataName(WellKnownTypes.FromKeyedServicesAttribute),
            compilation.GetTypeByMetadataName(WellKnownTypes.FromRouteAttribute),
            compilation.GetTypeByMetadataName(WellKnownTypes.FromQueryAttribute),
            compilation.GetTypeByMetadataName(WellKnownTypes.FromHeaderAttribute),
            compilation.GetTypeByMetadataName(WellKnownTypes.AsParametersAttribute),
            compilation.GetTypeByMetadataName(WellKnownTypes.ObsoleteAttribute),
            compilation.GetTypeByMetadataName(WellKnownTypes.FromFormAttribute),
            compilation.GetTypeByMetadataName(WellKnownTypes.IFormFile),
            compilation.GetTypeByMetadataName(WellKnownTypes.IFormFileCollection),
            compilation.GetTypeByMetadataName(WellKnownTypes.IFormCollection),
            compilation.GetTypeByMetadataName(WellKnownTypes.IBindableFromHttpContext),
            compilation.GetTypeByMetadataName(WellKnownTypes.HttpContext),
            compilation.GetTypeByMetadataName(WellKnownTypes.ParameterInfo)
        );
    }
}
