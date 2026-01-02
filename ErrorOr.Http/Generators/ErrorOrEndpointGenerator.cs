using System.Collections.Generic;
using System.Collections.Immutable;
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
#pragma warning disable RS1041 // Compiler extension targeting .NET 10.0 - intentional for this project
[Generator(LanguageNames.CSharp)]
#pragma warning restore RS1041
public sealed partial class ErrorOrEndpointGenerator : IIncrementalGenerator
{
    // Suppress EPS06: IncrementalValuesProvider is a struct with fluent API designed for method chaining
#pragma warning disable EPS06
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(EmitAttributeDefinition);

        // Create providers for each attribute type explicitly
        var baseProvider = CreateAttributeProvider(context, WellKnownTypes.ErrorOrEndpointAttribute);
        var getProvider = CreateAttributeProvider(context, WellKnownTypes.GetAttribute);
        var postProvider = CreateAttributeProvider(context, WellKnownTypes.PostAttribute);
        var putProvider = CreateAttributeProvider(context, WellKnownTypes.PutAttribute);
        var deleteProvider = CreateAttributeProvider(context, WellKnownTypes.DeleteAttribute);
        var patchProvider = CreateAttributeProvider(context, WellKnownTypes.PatchAttribute);

        // Register diagnostics for each provider
        RegisterDiagnostics(context, baseProvider);
        RegisterDiagnostics(context, getProvider);
        RegisterDiagnostics(context, postProvider);
        RegisterDiagnostics(context, putProvider);
        RegisterDiagnostics(context, deleteProvider);
        RegisterDiagnostics(context, patchProvider);

        // Combine all endpoints
        var endpoints = baseProvider.SelectMany(static (d, _) => d.Descriptors.Items).Collect()
            .Combine(getProvider.SelectMany(static (d, _) => d.Descriptors.Items).Collect())
            .Combine(postProvider.SelectMany(static (d, _) => d.Descriptors.Items).Collect())
            .Combine(putProvider.SelectMany(static (d, _) => d.Descriptors.Items).Collect())
            .Combine(deleteProvider.SelectMany(static (d, _) => d.Descriptors.Items).Collect())
            .Combine(patchProvider.SelectMany(static (d, _) => d.Descriptors.Items).Collect())
            .Select(static (combined, _) =>
            {
                var (((((e0, e1), e2), e3), e4), e5) = combined;
                var builder = ImmutableArray.CreateBuilder<EndpointDescriptor>(
                    e0.Length + e1.Length + e2.Length + e3.Length + e4.Length + e5.Length);
                builder.AddRange(e0);
                builder.AddRange(e1);
                builder.AddRange(e2);
                builder.AddRange(e3);
                builder.AddRange(e4);
                builder.AddRange(e5);
                return builder.ToImmutable();
            })
            .WithTrackingName(TrackingNames.EndpointCollection);

        context.RegisterSourceOutput(endpoints, static (spc, items) =>
        {
            if (!items.IsDefaultOrEmpty)
                EmitEndpoints(spc, items);
        });

        var jsonContexts = JsonContextProvider.Create(context).Collect();

        context.RegisterSourceOutput(
            endpoints.Combine(jsonContexts),
            static (spc, data) =>
            {
                var (eps, contexts) = data;
                if (!eps.IsDefaultOrEmpty)
                    AnalyzeJsonContextCoverage(spc, eps, contexts);
            });
    }

    private static void RegisterDiagnostics(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<EndpointData> provider)
    {
        context.RegisterSourceOutput(
            provider.SelectMany(static (data, _) => data.Diagnostics.Items),
            static (spc, diagnostic) => spc.ReportDiagnostic(diagnostic.ToDiagnostic()));
    }
#pragma warning restore EPS06

    private static IncrementalValuesProvider<EndpointData> CreateAttributeProvider(
        IncrementalGeneratorInitializationContext context,
        string attributeName)
    {
        return context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeName,
                static (n, _) => n is MethodDeclarationSyntax,
                static (ctx, _) => ExtractEndpointData(ctx));
    }

    private static void EmitAttributeDefinition(IncrementalGeneratorPostInitializationContext context)
    {
        context.AddSource("ErrorOrEndpointAttribute.g.cs", SourceText.From("""
                                                                           using System;

                                                                           namespace ErrorOr.Http
                                                                           {
                                                                               /// <summary>
                                                                               /// Base attribute for HTTP endpoints. Prefer shorthand: [Get], [Post], [Put], [Delete], [Patch].
                                                                               /// </summary>
                                                                               [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                                                                               public class ErrorOrEndpointAttribute : Attribute
                                                                               {
                                                                                   public ErrorOrEndpointAttribute(string httpMethod, string pattern = "/")
                                                                                   {
                                                                                       HttpMethod = httpMethod;
                                                                                       Pattern = pattern;
                                                                                   }

                                                                                   public string HttpMethod { get; }
                                                                                   public string Pattern { get; }
                                                                               }

                                                                               /// <summary>HTTP GET endpoint.</summary>
                                                                               [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                                                                               public sealed class GetAttribute : ErrorOrEndpointAttribute
                                                                               {
                                                                                   public GetAttribute(string pattern = "/") : base("GET", pattern) { }
                                                                               }

                                                                               /// <summary>HTTP POST endpoint.</summary>
                                                                               [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                                                                               public sealed class PostAttribute : ErrorOrEndpointAttribute
                                                                               {
                                                                                   public PostAttribute(string pattern = "/") : base("POST", pattern) { }
                                                                               }

                                                                               /// <summary>HTTP PUT endpoint.</summary>
                                                                               [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                                                                               public sealed class PutAttribute : ErrorOrEndpointAttribute
                                                                               {
                                                                                   public PutAttribute(string pattern = "/") : base("PUT", pattern) { }
                                                                               }

                                                                               /// <summary>HTTP DELETE endpoint.</summary>
                                                                               [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                                                                               public sealed class DeleteAttribute : ErrorOrEndpointAttribute
                                                                               {
                                                                                   public DeleteAttribute(string pattern = "/") : base("DELETE", pattern) { }
                                                                               }

                                                                               /// <summary>HTTP PATCH endpoint.</summary>
                                                                               [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
                                                                               public sealed class PatchAttribute : ErrorOrEndpointAttribute
                                                                               {
                                                                                   public PatchAttribute(string pattern = "/") : base("PATCH", pattern) { }
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
