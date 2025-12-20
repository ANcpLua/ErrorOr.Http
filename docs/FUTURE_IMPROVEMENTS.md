# Future Improvements for ErrorOr.Interceptors

## 1. Suppress AOT Warnings in Generated Code

Add `#pragma warning disable` to the generated code to suppress the IL2026/IL3050 warnings (they're false positives since ASP.NET's RequestDelegateGenerator handles them).

```csharp
// In generated code
#pragma warning disable IL2026, IL3050
group.MapGet("/{id}", (int id) => ErrorOrHttp.Map(...))
#pragma warning restore IL2026, IL3050
```

---

## 2. Smart Error Type Inference

Currently we emit all possible error status codes (400, 401, 403, 404, 409, 500). Instead, scan the method body for `Error.NotFound()`, `Error.Validation()`, etc. and only emit the relevant `.ProducesProblem()` calls.

```csharp
// Current: Always emits all
.ProducesValidationProblem()
.ProducesProblem(404)
.ProducesProblem(401)  // Unnecessary if not used
.ProducesProblem(403)  // Unnecessary if not used
...

// Better: Only emit what's actually used
.ProducesProblem(404)  // Only NotFound is used in this method
```

---

## 3. Async Handler Support Verification

Test and ensure async handlers work correctly:
- `Task<ErrorOr<T>>`
- `ValueTask<ErrorOr<T>>`
- Cancellation token support

---

## 4. Dependency Injection Support

Add `[FromServices]` parameter detection and proper DI wiring:

```csharp
[ErrorOrGet("/{id}")]
public static async Task<ErrorOr<User>> GetById(
    int id,
    [FromServices] IUserRepository repo)  // Auto-resolved from DI
```

---

## 5. Route Constraint Support

Support route constraints in the route pattern:

```csharp
[ErrorOrGet("/{id:int:min(1)}")]  // Route constraints
```

---

## 6. Authorization Attributes

Support `[Authorize]`, `[AllowAnonymous]` on endpoint methods:

```csharp
[ErrorOrGet("/{id}")]
[Authorize(Policy = "AdminOnly")]
public static ErrorOr<User> GetById(int id) => ...
```

Generated:
```csharp
group.MapGet("/{id}", ...).RequireAuthorization("AdminOnly");
```

---

## 7. Rate Limiting Support

```csharp
[ErrorOrGet("/")]
[RateLimit("fixed")]
public static ErrorOr<User[]> GetAll() => ...
```

---

## 8. Output Caching Support

```csharp
[ErrorOrGet("/{id}")]
[OutputCache(Duration = 60)]
public static ErrorOr<User> GetById(int id) => ...
```

---

## 9. Dual Generator Mode

Keep both generators, auto-detect which to use:
- **AOT mode** (`PublishAot=true`): Use attribute-based generator
- **JIT mode**: Use interceptor generator (zero-config)

```csharp
// In generator
var isAot = context.AnalyzerConfigOptionsProvider
    .Select((opts, _) => opts.GlobalOptions
        .TryGetValue("build_property.PublishAot", out var v) && v == "true");
```

---

## 10. Source Generator Diagnostics

Add helpful diagnostics:
- Warning if method isn't static
- Warning if return type isn't `ErrorOr<T>`
- Error if attribute used on non-method
- Info about generated endpoints

```csharp
context.ReportDiagnostic(Diagnostic.Create(
    new DiagnosticDescriptor("EOGEN001", "Non-static endpoint",
        "ErrorOr endpoint methods must be static", "Usage",
        DiagnosticSeverity.Warning, true),
    method.Locations[0]));
```

---

## 11. OpenAPI Description from XML Docs

Extract `<summary>` from XML documentation comments:

```csharp
/// <summary>
/// Gets a user by their unique identifier.
/// </summary>
[ErrorOrGet("/{id}")]
public static ErrorOr<User> GetById(int id) => ...
```

Generated:
```csharp
.WithDescription("Gets a user by their unique identifier.")
```

---

## 12. Request/Response Examples for OpenAPI

```csharp
[ErrorOrGet("/{id}")]
[ProducesResponseExample(typeof(User), "{ \"id\": 1, \"name\": \"John\" }")]
public static ErrorOr<User> GetById(int id) => ...
```

---

## 13. Endpoint Grouping Strategies

Support different grouping strategies:
- By class (current)
- By namespace
- By attribute tag
- Custom grouping

---

## 14. Middleware/Filter Support

Allow adding filters via attributes:

```csharp
[ErrorOrGet("/")]
[EndpointFilter<LoggingFilter>]
public static ErrorOr<User[]> GetAll() => ...
```

---

## 15. Unit Tests for New Generator

Add comprehensive tests:
- Snapshot tests for generated code
- Integration tests with actual HTTP calls
- AOT publish verification in CI

---

## Priority Ranking

| Priority | Item | Effort | Impact |
|----------|------|--------|--------|
| 1 | Suppress AOT warnings | Low | High |
| 2 | Unit tests | Medium | High |
| 3 | Smart error inference | Medium | Medium |
| 4 | Authorization support | Low | High |
| 5 | Dual generator mode | Medium | High |
| 6 | DI verification | Low | Medium |
| 7 | Diagnostics | Medium | Medium |
| 8 | XML docs → OpenAPI | Medium | Low |
