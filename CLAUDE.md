# ErrorOr.Http — Development Guide

> Roslyn source generator for AOT-safe ASP.NET Core Minimal API endpoints from `ErrorOr<T>` handlers.

## Quick Reference

```bash
dotnet build                    # Build all projects
dotnet test                     # Run tests (snapshot + unit)
dotnet pack ErrorOr.Http -c Release  # Create NuGet package
```

## Project Structure

```
ErrorOr.Http/
├── ErrorOr.Http/                    # Source generator (netstandard2.0)
│   ├── Generators/
│   │   ├── ErrorOrEndpointGenerator.cs           # Entry point + validation orchestration
│   │   ├── ErrorOrEndpointGenerator.Extractor.cs # Return type extraction
│   │   ├── ErrorOrEndpointGenerator.ParameterBinder.cs # Parameter classification
│   │   ├── ErrorOrEndpointGenerator.Emitter.cs   # Code generation
│   │   ├── ErrorOrEndpointGenerator.Analyzer.cs  # JSON context analysis
│   │   ├── RouteValidator.cs                     # Route pattern + binding validation
│   │   ├── DuplicateRouteDetector.cs            # Cross-endpoint duplicate detection
│   │   ├── Diagnostics.cs                        # EOE001-EOE025
│   │   ├── Models.cs                             # Data structures
│   │   └── WellKnownTypes.cs                    # Type name constants
│   └── Helpers/
│       └── EquatableArray.cs                    # Incremental generator caching
├── ErrorOr.Http.Tests/              # Unit tests (xUnit v3)
├── ErrorOr.Http.SnapShot/           # Snapshot tests (Verify)
└── ErrorOr.Http.Sample/             # Demo application
```

## Diagnostics Reference

### Handler Validation (EOE001-EOE002)

| ID     | Name              | Severity | Trigger                             |
|--------|-------------------|----------|-------------------------------------|
| EOE001 | InvalidReturnType | Error    | Handler doesn't return `ErrorOr<T>` |
| EOE002 | NonStaticHandler  | Error    | Handler method is not static        |

### Parameter Binding (EOE003-EOE005)

| ID     | Name                   | Severity | Trigger                                      |
|--------|------------------------|----------|----------------------------------------------|
| EOE003 | UnsupportedParameter   | Error    | Parameter cannot be bound                    |
| EOE004 | AmbiguousParameter     | Error    | Parameter needs explicit `[FromX]` attribute |
| EOE005 | MultipleBodyParameters | Error    | Multiple `[FromBody]` params                 |

### Body Source Conflicts (EOE006-EOE008)

| ID     | Name                       | Severity | Trigger                      |
|--------|----------------------------|----------|------------------------------|
| EOE006 | MultipleBodySources        | Error    | Mix of body/form/stream      |
| EOE007 | MultipleFromFormParameters | Error    | Multiple `[FromForm]` DTOs   |
| EOE008 | UnsupportedFormDtoShape    | Error    | Form DTO missing constructor |

### Form Binding (EOE009-EOE014)

| ID     | Name                            | Severity | Trigger                                |
|--------|---------------------------------|----------|----------------------------------------|
| EOE009 | FormFileNotNullable             | Warning  | Non-nullable `IFormFile`               |
| EOE010 | FormContentTypeRequired         | Info     | Endpoint uses form binding             |
| EOE013 | FormCollectionRequiresAttribute | Error    | `IFormCollection` without `[FromForm]` |
| EOE014 | UnsupportedFormType             | Error    | Invalid form parameter type            |

### Route Validation (EOE015-EOE020)

| ID     | Name                   | Severity | Trigger                                          |
|--------|------------------------|----------|--------------------------------------------------|
| EOE015 | RouteParameterNotBound | Error    | Route `{x}` has no matching method parameter     |
| EOE016 | DuplicateRoute         | Error    | Same route registered by multiple handlers       |
| EOE017 | InvalidRoutePattern    | Error    | Empty pattern, mismatched braces, empty `{}`     |
| EOE018 | UnboundRouteParameter  | Warning  | Potential name mismatch hint                     |
| EOE019 | EndpointNameCollision  | Warning  | Multiple endpoints get same OpenAPI operation ID |
| EOE020 | BodyOnReadOnlyMethod   | Warning  | `[FromBody]` on GET/HEAD/DELETE/OPTIONS          |

### OpenAPI & AOT (EOE021-EOE025)

| ID     | Name                        | Severity | Trigger                                |
|--------|-----------------------------|----------|----------------------------------------|
| EOE021 | UndocumentedErrorResponse   | Warning  | Error type not in OpenAPI metadata     |
| EOE022 | TypeNotInJsonContext        | Warning  | Type missing from `[JsonSerializable]` |
| EOE023 | RouteConstraintTypeMismatch | Warning  | `{id:guid}` but param is `int`         |
| EOE024 | PrimitiveTypeInJsonContext  | Hidden   | Primitive doesn't need registration    |
| EOE025 | SseErrorAfterStreamStart    | Info     | SSE errors can't be ProblemDetails     |

## Parameter Binding Order

1. **Explicit attributes**: `[FromRoute]`, `[FromQuery]`, `[FromHeader]`, `[FromBody]`, `[FromForm]`, `[FromServices]`,
   `[FromKeyedServices]`, `[AsParameters]`
2. **Special types**: `HttpContext`, `CancellationToken`, `Stream`, `PipeReader`
3. **Form types**: `IFormFile`, `IFormFileCollection`, `IFormCollection`
4. **Implicit route**: Parameter name matches route template `{name}`
5. **Implicit query**: Primitives and collections of primitives
6. **Custom binding**: Types with `TryParse` or `BindAsync`

## Error → HTTP Status Mapping

| ErrorOr Type           | Status | Response                       |
|------------------------|--------|--------------------------------|
| `Error.Validation()`   | 400    | `HttpValidationProblemDetails` |
| `Error.Unauthorized()` | 401    | `ProblemDetails`               |
| `Error.Forbidden()`    | 403    | `ProblemDetails`               |
| `Error.NotFound()`     | 404    | `ProblemDetails`               |
| `Error.Conflict()`     | 409    | `ProblemDetails`               |
| `Error.Failure()`      | 422    | `ProblemDetails`               |
| `Error.Unexpected()`   | 500    | `ProblemDetails`               |

## Success Type → HTTP Status

| Type                           | Status           |
|--------------------------------|------------------|
| `ErrorOr<T>`                   | 200 OK           |
| `ErrorOr<T>` (POST)            | 201 Created      |
| `ErrorOr<Created>`             | 201 Created      |
| `ErrorOr<Success>`             | 204 No Content   |
| `ErrorOr<Deleted>`             | 204 No Content   |
| `ErrorOr<Updated>`             | 204 No Content   |
| `ErrorOr<IAsyncEnumerable<T>>` | 200 (SSE stream) |

## Key Design Decisions

- **Static handlers only**: Instance methods are rejected (EOE002)
- **No implicit DI**: Services require `[FromServices]` (prevents runtime crashes)
- **Compile-time route validation**: Route/parameter mismatches are errors, not runtime failures
- **Zero-allocation errors**: `ToProblem()` uses `for` loops, not LINQ
- **Code-based inference**: Error types detected from method bodies, not XML docs
- **AOT-safe**: No reflection, fully trimmer-compatible
- **HTTP method normalization**: Always uppercase in generated code

## Common Tasks

### Adding a new diagnostic

1. Add descriptor in `Diagnostics.cs` with next available ID
2. Emit diagnostic at appropriate validation point:
    - Handler validation → `ErrorOrEndpointGenerator.cs` in `ExtractAndValidateEndpoint`
    - Route validation → `RouteValidator.cs`
    - Parameter binding → `ErrorOrEndpointGenerator.ParameterBinder.cs`
    - Cross-endpoint → `DuplicateRouteDetector.cs`
    - JSON/AOT → `ErrorOrEndpointGenerator.Analyzer.cs`
3. Add snapshot test in `SnapshotTests.cs`
4. Update this table above

### Adding a new parameter source

1. Add to `EndpointParameterSource` enum in `Models.cs`
2. Add detection in `ClassifyParameter()` in `ParameterBinder.cs`
3. Add emission in `EmitParameterBinding()` in `Emitter.cs`
4. Add tests

### Modifying generated code

1. Change emission in `Emitter.cs`
2. Run `dotnet test` — snapshot tests will fail
3. Review `.received.` files, copy to `.verified.` if correct

### Adding route validation

1. Add validation logic in `RouteValidator.cs`
2. Create diagnostic descriptor in `Diagnostics.cs`
3. Call from `ExtractAndValidateEndpoint` in main generator
4. Add tests

## Public API (Frozen)

These are stable and should not change without major version bump:

- Attributes: `[Get]`, `[Post]`, `[Put]`, `[Delete]`, `[Patch]`, `[ErrorOrEndpoint]`
- Extension: `MapErrorOrEndpoints(this IEndpointRouteBuilder)`
- Extension: `AddErrorOrEndpointJson<TContext>(this IServiceCollection)`
- Diagnostic IDs: EOE001-EOE025

## Release Process

```bash
# 1. Update version in ErrorOr.Http.csproj
# 2. Create and push tag
git tag -a v2.0.0 -m "Release v2.0.0"
git push origin v2.0.0
# 3. GitHub Actions automatically publishes to NuGet
```
