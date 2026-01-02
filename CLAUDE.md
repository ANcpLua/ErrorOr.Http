# ErrorOr.Http — Development Guide

> Roslyn source generator for AOT-safe ASP.NET Core Minimal API endpoints from `ErrorOr<T>` handlers.

## Quick Reference

```bash
dotnet build                    # Build all projects
dotnet test                     # Run 72 tests (11 snapshot + 61 unit)
dotnet pack ErrorOr.Http -c Release  # Create NuGet package
```

## Project Structure

```
ErrorOr.Http/
├── ErrorOr.Http/              # Source generator (netstandard2.0)
│   └── Generators/
│       ├── ErrorOrEndpointGenerator.cs      # Entry point
│       ├── ErrorOrEndpointGenerator.Extractor.cs  # Parameter classification
│       ├── ErrorOrEndpointGenerator.Emitter.cs    # Code generation
│       ├── ErrorOrEndpointGenerator.Analyzer.cs   # Error inference
│       ├── Diagnostics.cs                   # EOE001-EOE014
│       ├── Models.cs                        # Data structures
│       └── WellKnownTypes.cs               # Type name constants
├── ErrorOr.Http.Tests/        # Unit tests (xUnit v3)
├── ErrorOr.Http.SnapShot/     # Snapshot tests (Verify)
└── ErrorOr.Http.Sample/       # Demo application
```

## Diagnostics Reference

| ID | Name | Severity | Trigger |
|----|------|----------|---------|
| EOE001 | InvalidReturnType | Error | Handler doesn't return `ErrorOr<T>` |
| EOE002 | InvalidRouteTemplate | Error | Malformed route pattern |
| EOE003 | AmbiguousParameter | Error | Parameter could be route or query |
| EOE004 | MissingFromServices | Error | Service type without `[FromServices]` |
| EOE005 | MultipleBodyParameters | Error | Multiple `[FromBody]` params |
| EOE006 | MultipleBodySources | Error | Mix of body/form/stream |
| EOE007 | MultipleFromFormParameters | Error | Multiple `[FromForm]` DTOs |
| EOE008 | UnsupportedFormDtoShape | Error | Form DTO missing constructor |
| EOE009 | FormFileNotNullable | Warning | Non-nullable `IFormFile` |
| EOE010 | FormContentTypeRequired | Info | Endpoint uses form binding |
| EOE011 | MultipleFormDtos | Error | Duplicate of EOE007 |
| EOE013 | FormCollectionRequiresAttribute | Error | `IFormCollection` without `[FromForm]` |
| EOE014 | UnsupportedFormType | Error | Invalid form parameter type |

## Parameter Binding Order

1. **Explicit attributes**: `[FromRoute]`, `[FromQuery]`, `[FromHeader]`, `[FromBody]`, `[FromForm]`, `[FromServices]`, `[AsParameters]`
2. **Special types**: `HttpContext`, `CancellationToken`, `IFormFile`, `IFormFileCollection`, `IFormCollection`
3. **Implicit inference**: Route params by name match, primitives → query

## Error → HTTP Status Mapping

| ErrorOr Type | Status | Response |
|--------------|--------|----------|
| `Error.Validation()` | 400 | `HttpValidationProblemDetails` |
| `Error.Unauthorized()` | 401 | `ProblemDetails` |
| `Error.Forbidden()` | 403 | `ProblemDetails` |
| `Error.NotFound()` | 404 | `ProblemDetails` |
| `Error.Conflict()` | 409 | `ProblemDetails` |
| `Error.Failure()` | 422 | `ProblemDetails` |
| `Error.Unexpected()` | 500 | `ProblemDetails` |

## Success Type → HTTP Status

| Type | Status |
|------|--------|
| `ErrorOr<T>` | 200 OK |
| `ErrorOr<Created>` | 201 Created |
| `ErrorOr<Success>` | 204 No Content |
| `ErrorOr<Deleted>` | 204 No Content |
| `ErrorOr<Updated>` | 204 No Content |

## Key Design Decisions

- **No implicit DI**: Services require `[FromServices]` (prevents runtime crashes)
- **Zero-allocation errors**: `ToProblem()` uses `for` loops, not LINQ
- **Code-based inference**: Error types detected from method bodies, not XML docs
- **AOT-safe**: No reflection, fully trimmer-compatible

## Release Process

```bash
# 1. Update version in ErrorOr.Http.csproj
# 2. Create and push tag
git tag -a v1.0.1 -m "Release v1.0.1"
git push erroror-http v1.0.1
# 3. GitHub Actions automatically publishes to NuGet
```

## Common Tasks

### Adding a new diagnostic
1. Add descriptor in `Diagnostics.cs`
2. Emit in `Extractor.cs` at appropriate classification point
3. Add snapshot test in `SnapshotTests.cs`
4. Update this table above

### Adding a new parameter source
1. Add to `EndpointParameterSource` enum in `Models.cs`
2. Add detection in `ClassifyParameter()` in `Extractor.cs`
3. Add emission in `EmitParameterBinding()` in `Emitter.cs`
4. Add tests

### Modifying generated code
1. Change emission in `Emitter.cs`
2. Run `dotnet test` — snapshot tests will fail
3. Review `.received.` files, copy to `.verified.` if correct
