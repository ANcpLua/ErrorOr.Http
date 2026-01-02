using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using ANcpLua.Roslyn.Utilities.Testing;
using ErrorOr.Http.Generators;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace ErrorOr.Http.Tests;

/// <summary>
///     Configures test environment before any tests run.
/// </summary>
internal static class TestSetup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        TestConfiguration.ReferenceAssemblies = ReferenceAssemblies.Net.Net90
            .AddPackages([
                new PackageIdentity("ErrorOr", "2.0.1"),
                new PackageIdentity("Microsoft.AspNetCore.App.Ref", "10.0.1")
            ]);

        TestConfiguration.AdditionalReferences =
        [
            MetadataReference.CreateFromFile(typeof(ErrorOr<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ErrorOrEndpointGenerator).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(WebApplication).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IEndpointRouteBuilder).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ProblemDetails).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(HttpValidationProblemDetails).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(FromBodyAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(HttpContext).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(PipeReader).Assembly.Location)
        ];
    }
}
