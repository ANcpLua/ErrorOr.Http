using System.Runtime.CompilerServices;
using ANcpLua.Roslyn.Utilities.Testing;
using Microsoft.AspNetCore.Builder;
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
                new PackageIdentity("ErrorOr", "10.0.1"),
                new PackageIdentity("Microsoft.AspNetCore.App.Ref", "10.0.1")
            ]);

        TestConfiguration.AdditionalReferences =
        [
            MetadataReference.CreateFromFile(typeof(WebApplication).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ErrorOr<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IEndpointRouteBuilder).Assembly.Location)
        ];
    }
}
