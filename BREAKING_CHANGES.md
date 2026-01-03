# ErrorOr.Http v2.0 Breaking Changes

## New Diagnostics Added

| ID | Name | Severity | Description | Breaking? |
|----|------|----------|-------------|-----------|
| **EOE001** | InvalidReturnType | Error | Method marked with `[Get]` etc doesn't return `ErrorOr<T>` | Yes |
| **EOE002** | NonStaticHandler | Error | Handler method is not static | **Yes** - previously silent |
| **EOE015** | RouteParameterNotBound | Error | Route has `{x}` but no parameter captures it | **Yes** - previously runtime failure |
| **EOE016** | DuplicateRoute | Error | Same route registered by multiple handlers | **Yes** - previously last-wins |
| **EOE017** | InvalidRoutePattern | Error | Empty pattern, mismatched braces, empty `{}` | **Yes** - previously runtime error |
| **EOE018** | UnboundRouteParameter | Warning | Potential name mismatch between route and param | No |
| **EOE019** | EndpointNameCollision | Warning | Multiple endpoints get same OpenAPI operation ID | No |
| **EOE020** | BodyOnReadOnlyMethod | Warning | `[FromBody]` on GET/HEAD/DELETE/OPTIONS | No |
| **EOE023** | RouteConstraintTypeMismatch | Warning | `{id:guid}` but param is `int` | No |
| **EOE024** | PrimitiveTypeInJsonContext | Hidden | Primitive doesn't need `[JsonSerializable]` | No |
| **EOE025** | SseErrorAfterStreamStart | Info | SSE limitation - errors during enumeration can't be ProblemDetails | No |

## Behavior Changes

### 1. Non-Static Handlers Now Error (EOE002)
**Before:** Silently ignored, no endpoint registered
**After:** Compile-time error

```csharp
// This now produces EOE002
public class MyEndpoints
{
    [Get("/")]  // Error: Handler must be static
    public ErrorOr<string> Get() => "ok";
}
```

### 2. Route Parameter Validation (EOE015)
**Before:** Runtime 400 when route param not captured
**After:** Compile-time error

```csharp
[Get("/users/{userId}")]
public static ErrorOr<User> GetUser(int id) => ...;  // EOE015: Route has {userId} but no parameter captures it
```

### 3. Duplicate Route Detection (EOE016)
**Before:** Both registered, last one wins silently
**After:** Compile-time error on second handler

```csharp
// In EndpointsA.cs
[Get("/users/{id}")]
public static ErrorOr<User> GetUser(int id) => ...;

// In EndpointsB.cs - NOW ERRORS
[Get("/users/{id}")]  // EOE016: Route 'GET /users/{id}' is already registered by 'EndpointsA.GetUser'
public static ErrorOr<User> GetUserById(int id) => ...;
```

### 4. HTTP Method Case Normalization
**Before:** Passed through as-is, could cause issues
**After:** Always uppercase in generated code

```csharp
[ErrorOrEndpoint("get", "/users")]  // Normalized to "GET"
```

## Migration Guide

### Step 1: Fix Non-Static Handlers
```csharp
// Before
public class Endpoints
{
    [Get("/")] public ErrorOr<string> Get() => "ok";
}

// After
public static class Endpoints
{
    [Get("/")] public static ErrorOr<string> Get() => "ok";
}
```

### Step 2: Fix Route Parameter Names
```csharp
// Before - silent runtime failure
[Get("/users/{userId}")]
public static ErrorOr<User> GetUser(int id) => ...;

// After - Option A: Match name
[Get("/users/{userId}")]
public static ErrorOr<User> GetUser(int userId) => ...;

// After - Option B: Explicit binding
[Get("/users/{userId}")]
public static ErrorOr<User> GetUser([FromRoute(Name = "userId")] int id) => ...;
```

### Step 3: Fix Duplicate Routes
Rename or remove duplicate route handlers.

## Files Changed

1. **Diagnostics.cs** - All diagnostic descriptors (EOE001-EOE025)
2. **RouteValidator.cs** - New file for route pattern validation
3. **DuplicateRouteDetector.cs** - New file for cross-endpoint validation
4. **ErrorOrEndpointGenerator.cs** - Main generator with new validation hooks
5. **ErrorOrEndpointGenerator.Extractor.cs** - Return type extraction
6. **ErrorOrEndpointGenerator.ParameterBinder.cs** - Parameter binding logic (extracted)

## Test Additions Required

```csharp
// EOE002: Non-static handler
[Fact]
public Task NonStaticHandler_ReportsError()
{
    return """
        using ErrorOr;
        using ErrorOr.Http;
        
        public class Endpoints
        {
            [Get("/")]
            public ErrorOr<string> Get() => "ok";
        }
        """.ShouldHaveDiagnostics<ErrorOrEndpointGenerator>(
            Diagnostic("EOE002"));
}

// EOE015: Route parameter not bound
[Fact]
public Task RouteParameterNotBound_ReportsError()
{
    return """
        using ErrorOr;
        using ErrorOr.Http;
        
        public static class Endpoints
        {
            [Get("/users/{userId}")]
            public static ErrorOr<string> GetUser(int id) => "ok";
        }
        """.ShouldHaveDiagnostics<ErrorOrEndpointGenerator>(
            Diagnostic("EOE015"));
}

// EOE016: Duplicate route
[Fact]
public Task DuplicateRoute_ReportsError()
{
    return """
        using ErrorOr;
        using ErrorOr.Http;
        
        public static class EndpointsA
        {
            [Get("/users/{id}")]
            public static ErrorOr<string> GetUser(int id) => "ok";
        }
        
        public static class EndpointsB
        {
            [Get("/users/{id}")]
            public static ErrorOr<string> GetById(int id) => "ok";
        }
        """.ShouldHaveDiagnostics<ErrorOrEndpointGenerator>(
            Diagnostic("EOE016"));
}

// EOE020: Body on GET
[Fact]
public Task BodyOnGet_ReportsWarning()
{
    return """
        using ErrorOr;
        using ErrorOr.Http;
        using Microsoft.AspNetCore.Mvc;
        
        public static class Endpoints
        {
            [Get("/")]
            public static ErrorOr<string> Get([FromBody] string filter) => "ok";
        }
        """.ShouldHaveDiagnostics<ErrorOrEndpointGenerator>(
            Diagnostic("EOE020", DiagnosticSeverity.Warning));
}
```
