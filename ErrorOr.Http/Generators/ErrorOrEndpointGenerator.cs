using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ErrorOr.Http.Generators;

/// <summary>
///     Source generator for ASP.NET Core Minimal API endpoints that return ErrorOr&lt;T&gt;.
///     Generates RequestDelegate implementations with OpenAPI metadata and ProblemDetails error handling.
/// </summary>
/// <remarks>
///     This is a partial class split across multiple files following Microsoft's generator patterns:
///     - ErrorOrEndpointGenerator.cs - Pipeline setup (this file)
///     - ErrorOrEndpointGenerator.Extractor.cs - Data extraction logic
///     - ErrorOrEndpointGenerator.Emitter.cs - Code emission logic
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed partial class ErrorOrEndpointGenerator : IIncrementalGenerator
{
    private const string EndpointAttrName = "ErrorOr.Http.ErrorOrEndpointAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(EmitAttributeDefinition);

        var endpointData = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                EndpointAttrName,
                static (n, _) => n is MethodDeclarationSyntax,
                static (ctx, _) => ExtractEndpointData(ctx))
            .WithTrackingName(TrackingNames.EndpointMethods);

        var endpoints = endpointData
            .SelectMany(static (data, _) => data.Descriptors.Items)
            .WithTrackingName(TrackingNames.EndpointDescriptors)
            .Collect()
            .WithTrackingName(TrackingNames.EndpointCollection);

        var diagnostics = endpointData.SelectMany(static (data, _) => data.Diagnostics.Items);

        context.RegisterSourceOutput(diagnostics,
            static (spc, diagnostic) => spc.ReportDiagnostic(diagnostic.ToDiagnostic()));

        context.RegisterSourceOutput(endpoints, static (spc, items) =>
        {
            if (!items.IsDefaultOrEmpty)
                EmitEndpoints(spc, items);
        });
    }

    private static void EmitAttributeDefinition(IncrementalGeneratorPostInitializationContext context)
    {
        context.AddSource("ErrorOrEndpointAttribute.g.cs", SourceText.From("""
                                                                           using System;

                                                                           namespace ErrorOr.Http
                                                                           {
                                                                               [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                                                                               public sealed class ErrorOrEndpointAttribute : Attribute
                                                                               {
                                                                                   public ErrorOrEndpointAttribute(string httpMethod, string pattern)
                                                                                   {
                                                                                       HttpMethod = httpMethod;
                                                                                       Pattern = pattern;
                                                                                   }

                                                                                   public string HttpMethod { get; }
                                                                                   public string Pattern { get; }
                                                                               }
                                                                           }
                                                                           """, Encoding.UTF8));
    }

    private static class TrackingNames
    {
        public const string EndpointMethods = nameof(EndpointMethods);
        public const string EndpointDescriptors = nameof(EndpointDescriptors);
        public const string EndpointCollection = nameof(EndpointCollection);
    }
}
