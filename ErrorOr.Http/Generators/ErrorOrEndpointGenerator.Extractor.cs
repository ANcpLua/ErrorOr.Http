using System.Collections.Immutable;
using ErrorOr.Http.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ErrorOr.Http.Generators;

/// <summary>
///     Partial class containing extraction and parameter binding logic.
/// </summary>
public sealed partial class ErrorOrEndpointGenerator
{
    /// <summary>
    ///     Extracts the ErrorOr return type information from a method's return type.
    /// </summary>
    internal static ErrorOrReturnTypeInfo ExtractErrorOrReturnType(ITypeSymbol returnType)
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
        return constructedFrom is WellKnownTypes.TaskT or WellKnownTypes.ValueTaskT
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

    internal static EquatableArray<int> InferErrorTypesFromMethod(GeneratorAttributeSyntaxContext ctx, IMethodSymbol method)
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

                    if (!SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, semanticModel.Compilation.Assembly))
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

    internal static (bool IsObsolete, string? Message, bool IsError) GetObsoleteInfo(IMethodSymbol method, KnownSymbols symbols)
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

    /// <summary>
    ///     Result of extracting the ErrorOr return type, including SSE detection.
    /// </summary>
    internal readonly record struct ErrorOrReturnTypeInfo(
        string? SuccessTypeFqn,
        bool IsAsync,
        bool IsSse,
        string? SseItemTypeFqn,
        bool UsesSseItem);
}
