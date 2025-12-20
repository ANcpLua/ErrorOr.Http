# AGENTS.md - ErrorOr Interceptor Generator Final Release

## Mission

Cleanup dead code and update tests for the **completed** interceptor generator before NuGet release.

The generator intercepts `app.MapGet/Post/Put/Delete/Patch` calls returning `ErrorOr<T>` and auto-adds:
1. `.AddEndpointFilter()` - runtime error mapping to ProblemDetails
2. `.Produces<T>()` / `.ProducesProblem()` - OpenAPI metadata

**Zero attributes. Generator is feature-complete. This is cleanup only.**

---

## Current Working Files (DO NOT MODIFY LOGIC)

```
five/
├── Interceptors/
│   └── ErrorOrInterceptorGenerator.cs   ✅ DONE - detection + pipeline
├── Helpers/
│   ├── CodeBuilder.cs                   ✅ DONE - zero-allocation builder
│   ├── InterceptorEmitter.cs            ✅ DONE - code emission
│   ├── InterceptorTypes.cs              ✅ DONE - pipeline data types
│   └── EquatableArray.cs                ✅ DONE - incremental caching
├── build/
│   └── five.targets                     ✅ DONE - MSBuild integration
└── five.csproj                          ✅ DONE
```

---

## Task 1: Delete Dead Code

These files are from the **old attribute-based approach** and must be deleted:

| File | Reason |
|------|--------|
| `ErrorOrEndpointGenerator.cs` | Old attribute-based generator |
| `ErrorOrTypes.cs` | EndpointInfo/ParameterInfo for attributes |
| `ErrorOrRules.cs` | Diagnostics for attribute validation |
| `ErrorTypeInfo.cs` | Runtime enum extraction (unused) |
| `LocationInfo.cs` | DiagnosticInfo types (unused) |
| `SymbolExtensions.cs` | Attribute helpers (unused) |

**Verification:** After deletion, `dotnet build` must still succeed with 0 errors.

---

## Task 2: Update Tests

### Delete: `DiagnosticTests.cs`
No diagnostics in interceptor approach - delete entire file.

### Rewrite: `GeneratorCachingTests.cs`

Remove all `[ErrorOrEndpoints]`, `[ErrorOrGet]`, `[AllowedErrors]` attribute references.

Replace with interceptor-style tests:

```csharp
using ANcpLua.Interceptors.ErrorOr.Generator.Interceptors;

public class GeneratorCachingTests
{
    [Fact]
    public async Task Interceptor_WhenSourceUnchanged_Caches()
    {
        await """
            using ErrorOr;
            using Microsoft.AspNetCore.Builder;
            
            var app = WebApplication.Create();
            app.MapGet("/users/{id}", ErrorOr<User> (int id) =>
                id == 0 ? Error.NotFound() : new User(id));
            
            public record User(int Id);
            """.ShouldCache<ErrorOrInterceptorGenerator>();
    }

    [Fact]
    public async Task Interceptor_WhenEndpointAdded_Regenerates()
    {
        var original = """
            using ErrorOr;
            using Microsoft.AspNetCore.Builder;
            
            var app = WebApplication.Create();
            app.MapGet("/test", ErrorOr<string> () => "ok");
            """;

        var modified = """
            using ErrorOr;
            using Microsoft.AspNetCore.Builder;
            
            var app = WebApplication.Create();
            app.MapGet("/test", ErrorOr<string> () => "ok");
            app.MapPost("/test", ErrorOr<string> (string x) => x);
            """;

        await original.ShouldRegenerate<ErrorOrInterceptorGenerator>(modified);
    }

    [Fact]
    public async Task Interceptor_WhenUnrelatedFileAdded_DoesNotRegenerate()
    {
        var source = """
            using ErrorOr;
            using Microsoft.AspNetCore.Builder;
            
            var app = WebApplication.Create();
            app.MapGet("/stable", ErrorOr<int> () => 42);
            """;

        var unrelated = """
            public class UnrelatedService { }
            """;

        await source.ShouldNotRegenerate<ErrorOrInterceptorGenerator>(unrelated);
    }
}
```

### Rewrite: `SnapshotTests.cs`

Test the **generated interceptor output**:

```csharp
using ANcpLua.Interceptors.ErrorOr.Generator.Interceptors;
using Verify.SourceGenerators;

public class SnapshotTests
{
    [Fact]
    public Task SyncHandler_WithInferredErrors()
    {
        var source = """
            using ErrorOr;
            using Microsoft.AspNetCore.Builder;
            
            var app = WebApplication.Create();
            app.MapGet("/users/{id}", ErrorOr<User> (int id) =>
            {
                if (id <= 0) return Error.Validation("Invalid", "Bad ID");
                if (id == 404) return Error.NotFound("NotFound", "Missing");
                return new User(id, "Test");
            });
            
            public record User(int Id, string Name);
            """;

        return TestHelper.Verify<ErrorOrInterceptorGenerator>(source);
    }

    [Fact]
    public Task AsyncHandler_TaskErrorOr()
    {
        var source = """
            using ErrorOr;
            using Microsoft.AspNetCore.Builder;
            using System.Threading.Tasks;
            
            var app = WebApplication.Create();
            app.MapGet("/async", async Task<ErrorOr<string>> () =>
            {
                await Task.Delay(1);
                return "ok";
            });
            """;

        return TestHelper.Verify<ErrorOrInterceptorGenerator>(source);
    }

    [Fact]
    public Task MethodGroup_Reference()
    {
        var source = """
            using ErrorOr;
            using Microsoft.AspNetCore.Builder;
            
            var app = WebApplication.Create();
            app.MapGet("/health", GetHealth);
            
            static ErrorOr<string> GetHealth() => "healthy";
            """;

        return TestHelper.Verify<ErrorOrInterceptorGenerator>(source);
    }

    [Fact]
    public Task DeleteEndpoint_NoContent()
    {
        var source = """
            using ErrorOr;
            using Microsoft.AspNetCore.Builder;
            
            var app = WebApplication.Create();
            app.MapDelete("/users/{id}", ErrorOr<Deleted> (int id) =>
                id == 0 ? Error.NotFound() : Result.Deleted);
            """;

        return TestHelper.Verify<ErrorOrInterceptorGenerator>(source);
    }

    [Fact]
    public Task MultipleEndpoints_SameSignature_Grouped()
    {
        var source = """
            using ErrorOr;
            using Microsoft.AspNetCore.Builder;
            
            var app = WebApplication.Create();
            app.MapGet("/a", ErrorOr<string> () => "a");
            app.MapGet("/b", ErrorOr<string> () => "b");
            app.MapGet("/c", ErrorOr<string> () => "c");
            """;

        return TestHelper.Verify<ErrorOrInterceptorGenerator>(source);
    }

    [Fact]
    public Task NonErrorOr_Ignored()
    {
        var source = """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            
            var app = WebApplication.Create();
            app.MapGet("/ping", () => Results.Ok("pong"));
            """;

        // Should produce empty or minimal output
        return TestHelper.Verify<ErrorOrInterceptorGenerator>(source);
    }
}
```

---

## Task 3: Update .editorconfig

Remove attribute-based analyzer rules. Keep only generator-relevant:

```ini
[*.cs]
# RS - Roslyn analyzer rules
dotnet_diagnostic.RS1035.severity = warning  # Banned APIs (Environment.NewLine)
dotnet_diagnostic.RS1038.severity = warning  # netstandard2.0 target

# EPS - Struct copying (critical for pipeline data)
dotnet_diagnostic.EPS01.severity = warning
dotnet_diagnostic.EPS02.severity = warning
dotnet_diagnostic.EPS03.severity = warning
dotnet_diagnostic.EPS05.severity = warning
dotnet_diagnostic.EPS06.severity = warning

# EPC - Equality (critical for EquatableArray)
dotnet_diagnostic.EPC11.severity = warning
dotnet_diagnostic.EPC12.severity = warning
dotnet_diagnostic.EPC13.severity = warning
```

---

## Task 4: Update Directory.Packages.props

Remove unused packages, keep:

```xml
<ItemGroup>
  <!-- Generator core -->
  <PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="4.12.0" />
  <PackageVersion Include="PolySharp" Version="1.15.0" />
  
  <!-- Sample app -->
  <PackageVersion Include="ErrorOr" Version="2.0.1" />
  <PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.0" />
  
  <!-- Tests -->
  <PackageVersion Include="AwesomeAssertions" Version="9.0.0" />
  <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
  <PackageVersion Include="xunit.v3" Version="3.0.0" />
  <PackageVersion Include="Verify.SourceGenerators" Version="2.5.0" />
  <PackageVersion Include="Basic.Reference.Assemblies.Net90" Version="1.8.0" />
  <PackageVersion Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.12.0" />
  
  <!-- Analyzers -->
  <PackageVersion Include="ErrorProne.NET.Structs" Version="0.6.1-beta.1" />
  <PackageVersion Include="Roslynator.CodeAnalysis.Analyzers" Version="4.14.0" />
</ItemGroup>
```

---

## Constraints

| Rule | Reason |
|------|--------|
| No LINQ in generator | Performance |
| No HashSet in generator | netstandard2.0 compatibility |
| No Environment.NewLine | RS1035 violation |
| `global::` prefix in generated code | Avoid conflicts |
| `file` scoped generated types | Avoid conflicts |
| Filter `.g.cs` in predicate | Prevent infinite loops |
| Use AwesomeAssertions | Not FluentAssertions |
| Use `Should()` early pattern | AwesomeAssertions v9 style |

---

## Success Criteria

```bash
# Must pass:
dotnet build           # 0 errors, 0 warnings
dotnet test            # All tests pass
dotnet pack            # Creates valid NuGet package
```

### Verify Generated Output Contains:

1. `[InterceptsLocationAttribute(...)]` on each interceptor method
2. `.AddEndpointFilter(async (context, next) => ...)` - runtime mapping
3. `.Produces<T>(...)` - success type metadata
4. `.ProducesProblem(...)` or `.ProducesValidationProblem()` - error metadata
5. `file static class ErrorOrExtensions` with `ToProblem` method
6. All type references use `global::` prefix

### Files After Cleanup:

```
five/
├── Interceptors/
│   └── ErrorOrInterceptorGenerator.cs
├── Helpers/
│   ├── CodeBuilder.cs
│   ├── InterceptorEmitter.cs
│   ├── InterceptorTypes.cs
│   └── EquatableArray.cs
├── build/
│   └── five.targets
└── five.csproj

five.Tests/
├── GeneratorCachingTests.cs  (rewritten)
├── SnapshotTests.cs          (rewritten)
└── TestHelper.cs             (if needed)

five.Sample/
└── Program.cs                (unchanged)
```

---

## Do NOT

- Modify generator logic (ErrorOrInterceptorGenerator.cs)
- Modify emission logic (InterceptorEmitter.cs)
- Modify CodeBuilder implementation
- Add new features
- Change pipeline data types

This is **cleanup and test updates only**.