using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ErrorOr.Http.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ErrorOr.Http.Generators;

/// <summary>
///     Partial class containing all extraction logic for the endpoint generator.
/// </summary>
public sealed partial class ErrorOrEndpointGenerator
{
    #region EndpointDataExtractor

    private const string EndpointAttrFullName = "ErrorOr.Http.ErrorOrEndpointAttribute";

    internal static EndpointData ExtractEndpointData(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not IMethodSymbol { IsStatic: true } method)
            return EndpointData.Empty;

        var (successTypeFqn, isAsync) = ExtractErrorOrReturnType(method.ReturnType);
        if (successTypeFqn is null)
            return EndpointData.Empty;

        var inferredErrors = InferErrorTypesFromMethod(ctx, method);
        var knownSymbols = KnownSymbols.Create(ctx.SemanticModel.Compilation);

        return ProcessEndpointAttributes(ctx, method, successTypeFqn, isAsync, inferredErrors, knownSymbols);
    }


    private static EndpointData ProcessEndpointAttributes(
        GeneratorAttributeSyntaxContext ctx,
        IMethodSymbol method,
        string successTypeFqn,
        bool isAsync,
        EquatableArray<int> inferredErrors,
        KnownSymbols knownSymbols)
    {
        var descriptors = ImmutableArray.CreateBuilder<EndpointDescriptor>();
        var diagnostics = ImmutableArray.CreateBuilder<EndpointDiagnostic>();

        var (isObsolete, obsoleteMessage, isObsoleteError) = GetObsoleteInfo(method, knownSymbols);

        foreach (var descriptor in ctx.Attributes.Select(attr =>
                     TryCreateEndpointDescriptor(attr, method, successTypeFqn, isAsync, isObsolete, obsoleteMessage,
                         isObsoleteError, inferredErrors, diagnostics, knownSymbols)))
        {
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
        var attr = method.GetAttributes()
            .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, symbols.Obsolete));

        if (attr is null) return (false, null, false);

        var message = attr.ConstructorArguments.Length > 0 ? attr.ConstructorArguments[0].Value as string : null;
        var isError = attr.ConstructorArguments.Length > 1 && (bool)attr.ConstructorArguments[1].Value!;

        return (true, message, isError);
    }

    private static EndpointDescriptor? TryCreateEndpointDescriptor(
        AttributeData attr,
        IMethodSymbol method,
        string successTypeFqn,
        bool isAsync,
        bool isObsolete,
        string? obsoleteMessage,
        bool isObsoleteError,
        EquatableArray<int> inferredErrors,
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
            successTypeFqn,
            isAsync,
            method.ContainingType.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat),
            method.Name,
            isObsolete,
            obsoleteMessage,
            isObsoleteError,
            new EquatableArray<EndpointParameter>(parameterResult.Parameters),
            inferredErrors);
    }

    private static bool IsValidEndpointAttribute(
        AttributeData attr,
        out string httpMethod,
        out string pattern)
    {
        httpMethod = string.Empty;
        pattern = string.Empty;

        if (attr.AttributeClass?.ToDisplayString() != EndpointAttrFullName)
            return false;

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

    #endregion

    #region ReturnTypeAnalyzer

    private static (string? SuccessTypeFqn, bool IsAsync) ExtractErrorOrReturnType(ITypeSymbol returnType)
    {
        var (unwrapped, isAsync) = UnwrapAsyncType(returnType);

        if (!IsErrorOrType(unwrapped, out var errorOrType))
            return (null, false);

        var successTypeFqn = errorOrType.TypeArguments[0]
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return (successTypeFqn, isAsync);
    }

    private static (ITypeSymbol Type, bool IsAsync) UnwrapAsyncType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { IsGenericType: true } named)
            return (type, false);

        var constructedFrom = named.ConstructedFrom.ToDisplayString();

        return constructedFrom is "System.Threading.Tasks.Task<TResult>" or
            "System.Threading.Tasks.ValueTask<TResult>"
            ? (named.TypeArguments[0], true)
            : (type, false);
    }

    private static bool IsErrorOrType(ITypeSymbol type, out INamedTypeSymbol errorOrType)
    {
        errorOrType = null!;

        if (type is not INamedTypeSymbol { IsGenericType: true } named)
            return false;

        if (named.ConstructedFrom.ToDisplayString() != "ErrorOr.ErrorOr<TValue>")
            return false;

        errorOrType = named;
        return true;
    }

    #endregion

    #region ErrorTypeInferrer

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

                    // Only follow symbols in the same assembly to avoid infinite recursion or expensive lookups
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

    #endregion

    #region RouteParameterExtractor

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

    #endregion

    #region RoutePrimitiveResolver

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
            "global::System.Guid" => RoutePrimitiveKind.Guid,
            "global::System.DateTime" => RoutePrimitiveKind.DateTime,
            "global::System.DateTimeOffset" => RoutePrimitiveKind.DateTimeOffset,
            "global::System.DateOnly" => RoutePrimitiveKind.DateOnly,
            "global::System.TimeOnly" => RoutePrimitiveKind.TimeOnly,
            "global::System.TimeSpan" => RoutePrimitiveKind.TimeSpan,
            _ => null
        };
    }

    #endregion

    #region ParameterBinder

    private const string FromServicesAttrName = "Microsoft.AspNetCore.Mvc.FromServicesAttribute";

    private const string FromKeyedServicesAttrName =
        "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute";

    private const string FromBodyAttrName = "Microsoft.AspNetCore.Mvc.FromBodyAttribute";
    private const string FromRouteAttrName = "Microsoft.AspNetCore.Mvc.FromRouteAttribute";
    private const string FromQueryAttrName = "Microsoft.AspNetCore.Mvc.FromQueryAttribute";
    private const string FromHeaderAttrName = "Microsoft.AspNetCore.Mvc.FromHeaderAttribute";
    private const string AsParametersAttrName = "Microsoft.AspNetCore.Http.AsParametersAttribute";

    private static ParameterBindingResult BindParameters(
        IMethodSymbol method,
        ImmutableHashSet<string> routeParameters,
        ImmutableArray<EndpointDiagnostic>.Builder diagnostics,
        KnownSymbols knownSymbols)
    {
        if (method.Parameters.Length is 0) return ParameterBindingResult.Empty;

        var metas = BuildParameterMetas(method.Parameters, knownSymbols, diagnostics);

        if (metas.Count(m => m.HasFromBody) > 1)
        {
            diagnostics.Add(
                EndpointDiagnostic.Create(DiagnosticDescriptors.MultipleBodyParameters, method, method.Name));
            return ParameterBindingResult.Invalid;
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
        ImmutableArray<EndpointDiagnostic>.Builder diagnostics)
    {
        var type = parameter.Type;
        var typeFqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var hasFromRoute = HasParameterAttribute(parameter, knownSymbols.FromRoute, FromRouteAttrName);
        var hasFromQuery = HasParameterAttribute(parameter, knownSymbols.FromQuery, FromQueryAttrName);
        var hasFromHeader = HasParameterAttribute(parameter, knownSymbols.FromHeader, FromHeaderAttrName);
        var hasFromKeyedServices =
            HasParameterAttribute(parameter, knownSymbols.FromKeyedServices, FromKeyedServicesAttrName);
        var hasAsParameters = HasParameterAttribute(parameter, knownSymbols.AsParameters, AsParametersAttrName);

        var routeName = hasFromRoute ? TryGetFromRouteName(parameter, knownSymbols) ?? parameter.Name : parameter.Name;
        var queryName = hasFromQuery ? TryGetFromQueryName(parameter, knownSymbols) ?? parameter.Name : parameter.Name;
        var headerName =
            hasFromHeader ? TryGetFromHeaderName(parameter, knownSymbols) ?? parameter.Name : parameter.Name;
        var keyedServiceKey =
            hasFromKeyedServices ? ExtractKeyFromKeyedServiceAttribute(parameter, knownSymbols) : null;

        var (isNullable, isNonNullableValueType) = GetParameterNullability(type, parameter.NullableAnnotation);
        var (isCollection, itemType, itemPrimitiveKind) = AnalyzeCollectionType(type);

        return new ParameterMeta(
            index, parameter, parameter.Name, typeFqn, TryGetRoutePrimitiveKind(type),
            HasParameterAttribute(parameter, knownSymbols.FromServices, FromServicesAttrName),
            hasFromKeyedServices, keyedServiceKey,
            HasParameterAttribute(parameter, knownSymbols.FromBody, FromBodyAttrName),
            hasFromRoute, hasFromQuery, hasFromHeader, hasAsParameters,
            routeName, queryName, headerName,
            typeFqn == "global::System.Threading.CancellationToken",
            typeFqn == "global::Microsoft.AspNetCore.Http.HttpContext",
            isNullable, isNonNullableValueType,
            isCollection, itemType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), itemPrimitiveKind);
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
        var attr = parameter.GetAttributes().FirstOrDefault(a =>
            SymbolEqualityComparer.Default.Equals(a.AttributeClass, knownSymbols.FromKeyedServices));

        if (attr is null || attr.ConstructorArguments.Length == 0) return null;

        var val = attr.ConstructorArguments[0].Value;
        return val switch { string s => $"\"{s}\"", _ => val?.ToString() };
    }

    private static bool HasParameterAttribute(IParameterSymbol parameter, INamedTypeSymbol? attributeSymbol,
        string attributeName)
    {
        // 1. Symbol match (Robust)
        if (attributeSymbol is not null &&
            parameter.GetAttributes()
                .Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attributeSymbol)))
            return true;

        // 2. String match (Fallback for tests/incomplete compilations)
        var shortName = attributeName[(attributeName.LastIndexOf('.') + 1)..];
        var shortNameWithoutAttr = shortName.EndsWith("Attribute")
            ? shortName[..^"Attribute".Length]
            : shortName;

        return parameter.GetAttributes().Any(attr =>
        {
            var display = attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (display is null) return false;

            // Normalize: remove global:: prefix
            if (display.StartsWith("global::"))
                display = display[8..];

            return display == attributeName ||
                   display.EndsWith($".{shortName}") ||
                   display == shortName ||
                   display == shortNameWithoutAttr;
        });
    }

    private static string? TryGetFromRouteName(IParameterSymbol parameter, KnownSymbols knownSymbols)
    {
        var attr = parameter.GetAttributes()
            .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, knownSymbols.FromRoute));
        return ExtractNameFromAttribute(attr);
    }

    private static string? TryGetFromQueryName(IParameterSymbol parameter, KnownSymbols knownSymbols)
    {
        var attr = parameter.GetAttributes()
            .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, knownSymbols.FromQuery));
        return ExtractNameFromAttribute(attr);
    }

    private static string? TryGetFromHeaderName(IParameterSymbol parameter, KnownSymbols knownSymbols)
    {
        var attr = parameter.GetAttributes()
            .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, knownSymbols.FromHeader));
        return ExtractNameFromAttribute(attr);
    }

    private static string? ExtractNameFromAttribute(AttributeData? attr)
    {
        if (attr is null) return null;

        var namedValue = attr.NamedArguments
            .Where(static n => n.Key == "Name")
            .Select(static n => n.Value.Value as string)
            .FirstOrDefault(static v => !string.IsNullOrWhiteSpace(v));

        if (namedValue is not null) return namedValue;

        return attr.ConstructorArguments.Length > 0 ? attr.ConstructorArguments[0].Value as string : null;
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
        // 1. Explicit Attributes (Always win)
        if (meta.HasAsParameters)
            return ClassifyAsParameters(in meta, routeParameters, method, diagnostics, knownSymbols);
        if (meta.HasFromBody) return ParameterSuccess(in meta, EndpointParameterSource.Body);
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

        // 2. Implicit Special Types
        if (meta.IsHttpContext) return ParameterSuccess(in meta, EndpointParameterSource.HttpContext);
        if (meta.IsCancellationToken) return ParameterSuccess(in meta, EndpointParameterSource.CancellationToken);

        // 3. Implicit Route (Name matches pattern)
        if (routeParameters.Contains(meta.Name)) return ClassifyImplicitRouteParameter(in meta, method, diagnostics);

        // 4. Implicit Query (With the Grain: Primitives & Collections default to Query if not in route)
        if (meta.RouteKind is not null || (meta.IsCollection && meta.CollectionItemPrimitiveKind is not null))
            return ParameterSuccess(in meta, EndpointParameterSource.Query, queryName: meta.Name);

        // 5. BRUTAL MODE: No guessing. If it's not a primitive and not labeled, ERROR.
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
        // 1. Find Primary Constructor or Single Public Constructor
        if (meta.Symbol.Type is not INamedTypeSymbol typeSymbol)
            return EmitParameterError(in meta, method, diagnostics,
                "[AsParameters] can only be used on class or struct types.");

        var constructor = typeSymbol.Constructors
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault(); // Simplified selection: greedily pick longest constructor. Ideally match C# primary ctor logic.

        if (constructor is null)
            return EmitParameterError(in meta, method, diagnostics, "[AsParameters] type must have a constructor.");

        // 2. Build Metas for Constructor Parameters
        var children = ImmutableArray.CreateBuilder<EndpointParameter>();
        for (var i = 0; i < constructor.Parameters.Length; i++)
        {
            var paramSymbol = constructor.Parameters[i];
            var childMeta =
                CreateParameterMeta(i, paramSymbol, knownSymbols, diagnostics); // Reusing CreateParameterMeta logic

            // Recursive Call
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
        string? keyedServiceKey = null)
    {
        return new ParameterClassificationResult(false, new EndpointParameter(
            meta.Name,
            meta.TypeFqn,
            source,
            routeName ?? queryName ?? headerName ?? keyedServiceKey,
            meta.IsNullable,
            meta.IsNonNullableValueType,
            meta.IsCollection,
            meta.CollectionItemTypeFqn,
            EquatableArray<EndpointParameter>.Empty));
    }

    private static ParameterClassificationResult ClassifyFromRouteParameter(
        in ParameterMeta meta,
        ImmutableHashSet<string> routeParameters,
        IMethodSymbol method,
        ImmutableArray<EndpointDiagnostic>.Builder diagnostics)
    {
        if (meta.RouteKind is null)
        {
            return EmitParameterError(in meta, method, diagnostics,
                "[FromRoute] requires a supported route primitive type");
        }

        if (!routeParameters.Contains(meta.RouteName))
        {
            return EmitParameterError(in meta, method, diagnostics,
                $"route parameter '{meta.RouteName}' not found in pattern");
        }

        return ParameterSuccess(in meta, EndpointParameterSource.Route, meta.RouteName);
    }

    private static ParameterClassificationResult ClassifyImplicitRouteParameter(
        in ParameterMeta meta,
        IMethodSymbol method,
        ImmutableArray<EndpointDiagnostic>.Builder diagnostics)
    {
        return meta.RouteKind is null
            ? EmitParameterError(in meta, method, diagnostics, "route parameters must use supported primitive types")
            : ParameterSuccess(in meta, EndpointParameterSource.Route, meta.Name);
    }


    private readonly record struct ParameterClassificationResult(bool IsError, EndpointParameter Parameter)
    {
        public static readonly ParameterClassificationResult Error = new(true, default);
    }

    #endregion
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
    INamedTypeSymbol? Obsolete)
{
    public static KnownSymbols Create(Compilation compilation)
    {
        return new KnownSymbols(
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromBodyAttribute"),
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromServicesAttribute"),
            compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute"),
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromRouteAttribute"),
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromQueryAttribute"),
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromHeaderAttribute"),
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.AsParametersAttribute"),
            compilation.GetTypeByMetadataName("System.ObsoleteAttribute")
        );
    }
}
