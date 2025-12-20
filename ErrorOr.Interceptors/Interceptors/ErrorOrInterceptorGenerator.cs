using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using ErrorOr.Interceptors.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ErrorOr.Interceptors;

[Generator(LanguageNames.CSharp)]
public sealed class ErrorOrInterceptorGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var enabled = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) => IsGeneratorEnabled(options))
            .WithTrackingName(TrackingNames.Configuration);

        var callSites = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsMapInvocation(node),
                transform: static (ctx, ct) => TryExtractCallSite(ctx, ct))
            .Where(static x => x.HasValue)
            .Select(static (x, _) => x!.Value)
            .WithTrackingName(TrackingNames.CallSites);

        var needsInterceptsAttr = context.CompilationProvider
            .Select(static (c, _) => c.GetTypeByMetadataName(TypeNames.InterceptsLocationAttribute) is null);

        var grouped = callSites
            .Collect()
            .Combine(enabled)
            .Select(static (pair, _) => pair is { Right: true, Left.IsDefaultOrEmpty: false }
                ? GroupCallSites(pair.Left)
                : ImmutableArray<InterceptorGroup>.Empty)
            .WithTrackingName(TrackingNames.GroupedCalls);

        context.RegisterSourceOutput(
            grouped.Combine(needsInterceptsAttr).WithTrackingName(TrackingNames.FinalOutput),
            static (spc, pair) =>
            {
                if (!pair.Left.IsEmpty)
                    InterceptorEmitter.Emit(spc, pair.Left, pair.Right);
            });
    }

    private static bool IsGeneratorEnabled(AnalyzerConfigOptionsProvider options) =>
        !options.GlobalOptions.TryGetValue(ConfigKeys.InterceptorEnabled, out var v) ||
        !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);

    internal static bool IsMapInvocation(SyntaxNode node) =>
        node is InvocationExpressionSyntax
        {
            SyntaxTree: var tree,
            Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: var name }
        } && !tree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal) && IsMapMethodName(name);

    private static bool IsMapMethodName(string name) =>
        name is "MapGet" or "MapPost" or "MapPut" or "MapDelete" or "MapPatch";

    private static MapCallSite? TryExtractCallSite(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;

        if (ctx.SemanticModel.GetOperation(invocation, ct) is not IInvocationOperation operation)
            return null;

        if (!IsEndpointRouteBuilderMethod(operation.TargetMethod))
            return null;

        var handlerArg = FindHandlerArgument(operation);
        if (handlerArg is null)
            return null;

        var (handlerType, handlerSyntax) = GetHandlerInfo(handlerArg);
        if (handlerType is null)
            return null;

        var (successType, isAsync) = ExtractErrorOrType(handlerType);
        if (successType is null)
            return null;

        return new MapCallSite(
            HttpMethod: GetHttpMethodFromName(operation.TargetMethod.Name),
            SuccessTypeFqn: successType,
            IsAsync: isAsync,
            InferredErrorTypes: InferErrorTypes(handlerSyntax, handlerArg),
            EncodedLocation: GetEncodedLocation(ctx, invocation, ct));
    }

    private static IArgumentOperation? FindHandlerArgument(IInvocationOperation operation)
    {
        if (operation.Arguments.Length < 2)
            return null;

        // 1. Try finding by name "handler"
        foreach (var arg in operation.Arguments)
        {
            if (arg.Parameter?.Name == "handler")
                return arg;
        }

        // 2. Fallback: The handler is always the last argument
        // (Extension methods: [Receiver, Pattern, ..., Handler])
        return operation.Arguments[^1];
    }

    private static bool IsEndpointRouteBuilderMethod(IMethodSymbol method)
    {
        if (!IsMapMethodName(method.Name)) return false;
        if (method is not { IsExtensionMethod: true, Parameters.Length: >= 2 })
            return method.ContainingType?.ToDisplayString().Contains("EndpointRouteBuilder") ?? false;
        var receiverType = method.Parameters[0].Type.ToDisplayString();
        if (receiverType.Contains("IEndpointRouteBuilder")) return true;
        return method.ContainingType?.ToDisplayString().Contains("EndpointRouteBuilder") ?? false;
    }

    private static string GetEncodedLocation(GeneratorSyntaxContext ctx, InvocationExpressionSyntax invocation, CancellationToken ct)
    {
        var interceptable = ctx.SemanticModel.GetInterceptableLocation(invocation, ct);
        if (interceptable is not null) return interceptable.GetInterceptsLocationAttributeSyntax();
        var span = invocation.GetLocation().GetLineSpan();
        var line = span.StartLinePosition.Line + 1;
        var column = span.StartLinePosition.Character + 1;
        return $"[global::System.Runtime.CompilerServices.InterceptsLocationAttribute(1, \"{line}:{column}\")]";
    }

    private static (ITypeSymbol? ReturnType, SyntaxNode? HandlerSyntax) GetHandlerInfo(IArgumentOperation handlerArg)
    {
        return handlerArg.Value switch
        {
            IDelegateCreationOperation del => ExtractFromDelegate(del),
            IConversionOperation conv => ExtractFromConversion(conv),
            IMethodReferenceOperation methodRef => (methodRef.Method.ReturnType, GetMethodBody(methodRef.Method)),
            IAnonymousFunctionOperation anon => (anon.Symbol.ReturnType, anon.Syntax),
            { Type: INamedTypeSymbol { DelegateInvokeMethod: { } invoke } } => (invoke.ReturnType, null),
            _ => (null, null)
        };
    }

    private static (ITypeSymbol? ReturnType, SyntaxNode? HandlerSyntax) ExtractFromDelegate(IDelegateCreationOperation delegateOp)
    {
        return delegateOp.Target switch
        {
            IAnonymousFunctionOperation anon => (anon.Symbol.ReturnType, anon.Syntax),
            IMethodReferenceOperation methodRef => (methodRef.Method.ReturnType, GetMethodBody(methodRef.Method)),
            _ => (null, null)
        };
    }

    private static (ITypeSymbol? ReturnType, SyntaxNode? HandlerSyntax) ExtractFromConversion(IConversionOperation conversionOp)
    {
        return conversionOp.Operand switch
        {
            IDelegateCreationOperation innerDel => ExtractFromDelegate(innerDel),
            IAnonymousFunctionOperation anon => (anon.Symbol.ReturnType, anon.Syntax),
            _ => (null, null)
        };
    }

    private static SyntaxNode? GetMethodBody(IMethodSymbol method)
    {
        if (method.DeclaringSyntaxReferences.IsDefaultOrEmpty) return null;
        return method.DeclaringSyntaxReferences[0].GetSyntax() switch
        {
            MethodDeclarationSyntax m => m.Body ?? (SyntaxNode?)m.ExpressionBody,
            LocalFunctionStatementSyntax f => f.Body ?? (SyntaxNode?)f.ExpressionBody,
            var other => other
        };
    }

    private static (string? SuccessTypeFqn, bool IsAsync) ExtractErrorOrType(ITypeSymbol returnType)
    {
        var (unwrapped, isAsync) = UnwrapAsyncType(returnType);
        if (unwrapped is not INamedTypeSymbol { IsGenericType: true } errorOrType ||
            errorOrType.ConstructedFrom.ToDisplayString() != "ErrorOr.ErrorOr<TValue>")
            return (null, false);
        var successTypeFqn = errorOrType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return (successTypeFqn, isAsync);
    }

    private static (ITypeSymbol Type, bool IsAsync) UnwrapAsyncType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { IsGenericType: true } named) return (type, false);
        var typeName = named.ConstructedFrom.ToDisplayString();
        return typeName is "System.Threading.Tasks.Task<TResult>" or "System.Threading.Tasks.ValueTask<TResult>"
            ? (named.TypeArguments[0], true)
            : (type, false);
    }

    private static string GetHttpMethodFromName(string methodName) =>
        methodName switch { "MapGet" => "Get", "MapPost" => "Post", "MapPut" => "Put", "MapDelete" => "Delete", "MapPatch" => "Patch", _ => "Get" };

    private static EquatableArray<int> InferErrorTypes(SyntaxNode? handlerSyntax, IArgumentOperation handlerArg)
    {
        var bodyToScan = handlerSyntax ?? (handlerArg.Syntax is ArgumentSyntax { Expression: var expr } ? expr : null);
        if (bodyToScan is null) return EquatableArray<int>.Empty;
        var errorTypes = CollectErrorFactoryCalls(bodyToScan);
        if (errorTypes.Count == 0) return EquatableArray<int>.Empty;
        errorTypes.Sort();
        return new EquatableArray<int>([.. errorTypes]);
    }

    private static List<int> CollectErrorFactoryCalls(SyntaxNode body)
    {
        var errorTypes = new List<int>();
        foreach (var node in body.DescendantNodes())
        {
            if (TryGetErrorFactoryType(node) is { } errorType && !errorTypes.Contains(errorType))
                errorTypes.Add(errorType);
        }
        return errorTypes;
    }

    private static int? TryGetErrorFactoryType(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.Text: "Error" },
                    Name: IdentifierNameSyntax { Identifier.Text: var factoryName }
                }
            })
            return null;
        var errorType = MapErrorFactoryToType(factoryName);
        return errorType >= 0 ? errorType : null;
    }

    private static int MapErrorFactoryToType(string factoryName) =>
        factoryName switch
        {
            "Failure" => 0, "Unexpected" => 1, "Validation" => 2, "Conflict" => 3, "NotFound" => 4, "Unauthorized" => 5, "Forbidden" => 6, _ => -1
        };

    internal static ImmutableArray<InterceptorGroup> GroupCallSites(ImmutableArray<MapCallSite> callSites)
    {
        var groups = new Dictionary<InterceptorSignature, List<string>>(InterceptorSignatureComparer.Instance);
        foreach (var site in callSites)
        {
            var signature = new InterceptorSignature(site.HttpMethod, site.SuccessTypeFqn, site.IsAsync, site.InferredErrorTypes);
            if (!groups.TryGetValue(signature, out var locations)) { locations = []; groups[signature] = locations; }
            locations.Add(site.EncodedLocation);
        }
        return BuildSortedGroups(groups);
    }

    private static ImmutableArray<InterceptorGroup> BuildSortedGroups(Dictionary<InterceptorSignature, List<string>> groups)
    {
        var keys = new List<InterceptorSignature>(groups.Keys);
        keys.Sort((a, b) => { var cmp = string.CompareOrdinal(a.HttpMethod, b.HttpMethod); return cmp != 0 ? cmp : string.CompareOrdinal(a.SuccessTypeFqn, b.SuccessTypeFqn); });
        var builder = ImmutableArray.CreateBuilder<InterceptorGroup>(groups.Count);
        foreach (var key in keys) { var locations = groups[key]; locations.Sort(StringComparer.Ordinal); builder.Add(new InterceptorGroup(key, new EquatableArray<string>([.. locations]))); }
        return builder.ToImmutable();
    }

    private static class ConfigKeys { public const string InterceptorEnabled = "dotnet_diagnostic.ErrorOrInterceptor.enabled"; }
    private static class TypeNames { public const string InterceptsLocationAttribute = "System.Runtime.CompilerServices.InterceptsLocationAttribute"; }
    private static class TrackingNames { public const string Configuration = nameof(Configuration); public const string CallSites = nameof(CallSites); public const string GroupedCalls = nameof(GroupedCalls); public const string FinalOutput = nameof(FinalOutput); }
}
