# ErrorOr.Http — ASP.NET Core Implementation Reference

**Target Framework:** .NET 10  
**Last Updated:** December 2025  
**Source:** [Microsoft Learn - Minimal APIs](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis)

---

## Table of Contents

1. [Breaking Changes & .NET 10 Updates](#breaking-changes--net-10-updates)
2. [Implementation Roadmap](#implementation-roadmap)
3. [Parameter Binding Specification](#parameter-binding-specification)
4. [Reference Documentation](#reference-documentation)
5. [Design Decisions](#design-decisions)

---

## Breaking Changes & .NET 10 Updates

### 🔥 CRITICAL: Authentication Behavior Change

**What Changed:**
Starting with .NET 10, ASP.NET Core automatically detects API endpoints and returns proper HTTP status codes instead of
redirecting to login pages.

| Endpoint Type                     | .NET 9                 | .NET 10              |
|-----------------------------------|------------------------|----------------------|
| `MapGet/MapPost/MapPut/MapDelete` | 302 → `/Account/Login` | **401 Unauthorized** |
| `[ApiController]`                 | 302 → `/Account/Login` | **401 Unauthorized** |
| Razor Pages                       | 302 → `/Account/Login` | 302 (unchanged)      |

**Detection Criteria (Automatic):**
ASP.NET Core 10 treats these as API endpoints:

1. All `MapVerb()` endpoints (MapGet, MapPost, etc.)
2. Controllers with `[ApiController]`
3. Endpoints explicitly requesting JSON responses
4. SignalR hubs

**Impact on ErrorOr.Http:**
All generated endpoints automatically return `401 Unauthorized` or `403 Forbidden` instead of redirecting. This is *
*correct REST API behavior** — no code changes required.

**Source:
** [API endpoint authentication behavior in ASP.NET Core](https://learn.microsoft.com/aspnet/core/security/authentication/cookie#api-endpoint-authentication-behavior)

---

### Other .NET 10 Changes

| Feature                 | Change                                       | Impact on ErrorOr.Http              |
|-------------------------|----------------------------------------------|-------------------------------------|
| **Built-in Validation** | `builder.Services.AddValidation()` available | ✅ Use built-in instead of custom   |
| **OpenAPI 3.1**         | Default generation upgraded from 3.0         | ✅ Already compatible                |
| **Native XML Docs**     | Native population of OpenAPI from XML        | ✅ No custom generator needed        |
| **Empty Form Values**   | Bind to `null` for nullable types            | ✅ Simplifies `[FromForm]` logic    |
| **SSE Support**         | `TypedResults.ServerSentEvents` added        | 💡 Future: `ErrorOr<SseItem<T>>`    |

---

## Implementation Roadmap

### Current Status: v1.0 (Stable)

**Supported:**

- ✅ Route parameters (`{id}`, `{slug}`)
- ✅ Query parameters (primitives & collections e.g. `?ids=1&ids=2`)
- ✅ Header binding (`[FromHeader]`)
- ✅ Body binding (`[FromBody]`)
- ✅ Service injection (`[FromServices]`, `[FromKeyedServices]`)
- ✅ Special types (`HttpContext`, `CancellationToken`)
- ✅ Error inference from code (not XML docs)
- ✅ Async handlers (`Task<ErrorOr<T>>`, `ValueTask<ErrorOr<T>>`)
- ✅ OpenAPI metadata generation
- ✅ **Implicit Query**: Primitives not in route default to Query (no attribute needed)
- ✅ **Brutal Safety**: Ambiguous parameters trigger compile errors (no runtime DI crashes)
- ✅ `[AsParameters]` recursive parameter binding
- ✅ Form binding (`[FromForm]` primitives and DTOs, `IFormFile`, `IFormFileCollection`)

**Not Supported (v2.0 Roadmap):**

- ❌ Custom binding (`TryParse`, `BindAsync`, `IBindableFromHttpContext<T>`)
- ❌ Stream/PipeReader body binding

---

### Priority Implementation Order

| Priority | Feature                      | Status          | Est. Time | Blocker     |
|----------|------------------------------|-----------------|-----------|-------------|
| **DONE** | Route template parsing       | ✅ Completed    | -         | -           |
| **DONE** | Special types detection      | ✅ Completed    | -         | -           |
| **DONE** | Array/collection binding     | ✅ Completed    | -         | -           |
| **DONE** | Implicit Query Binding       | ✅ Completed    | -         | -           |
| **DONE** | AsParameters recursion       | ✅ Completed    | -         | -           |
| **DONE** | Form binding + antiforgery   | ✅ Completed    | -         | -           |
| 6        | SSE / Streaming Support      | 💡 Researching  | 60 min    | -           |

---

## Parameter Binding Specification

### Binding Precedence (Official Microsoft Order)

**Source:
** [Binding Precedence - Microsoft Learn](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/parameter-binding#binding-precedence)

```
1. Explicit attributes (From* attributes):
   [FromRoute] → [FromQuery] → [FromHeader] → [FromBody] → 
   [FromForm] → [FromServices] → [AsParameters]

2. Special types (auto-detected):
   HttpContext → HttpRequest → HttpResponse → ClaimsPrincipal → 
   CancellationToken → IFormCollection → IFormFileCollection → 
   IFormFile → Stream → PipeReader

3. Custom binding methods:
   - IBindableFromHttpContext<T>.BindAsync
   - Static BindAsync(HttpContext, ParameterInfo)
   - Static TryParse(string, out T)

4. Implicit inference:
   - If parameter name exists in route template → bind from route
   - Else if primitive type → bind from query string
   - Else if type registered in DI → bind from services
   - Else → bind from body (POST/PUT/PATCH only)
```

**ErrorOr.Http Design Decision:**
We **disable step 4 (implicit inference)** for services to prevent runtime DI failures. Instead, we emit **diagnostic
EOE004** requiring explicit `[FromServices]`.

---

### Priority 0: Route Template Parsing (CRITICAL)

**Purpose:** Determine which parameters bind from route vs query string.

**Pattern:** `/users/{id:int}/posts/{slug}`

**Detection:**

```regex
\{([^:}]+)(?::[^}]+)?\}
```

**Example:**

```csharp
[Get("/users/{id}/posts/{slug}")]
public static ErrorOr<Post> GetPost(
    int id,      // ← Route (matches {id})
    string slug, // ← Route (matches {slug})
    int? page)   // ← Query (not in template)
```

**Constraints Supported:**

- `{id:int}` → int
- `{id:guid}` → Guid
- `{slug:regex(^[a-z0-9_-]+$)}` → string with validation

**Reference:** `/aspnetcore/src/Http/Routing/src/Patterns/RoutePatternParser.cs`

---

### Priority 1: Special Types (15 min)

**Spec:
** [Special types - Microsoft Learn](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/parameter-binding#special-types)

**Types that bind without attributes:**

| Type                  | Binding Source                          | Example               |
|-----------------------|-----------------------------------------|-----------------------|
| `HttpContext`         | `context`                               | Full request context  |
| `HttpRequest`         | `context.Request`                       | Request details       |
| `HttpResponse`        | `context.Response`                      | Response manipulation |
| `ClaimsPrincipal`     | `context.User`                          | Authenticated user    |
| `CancellationToken`   | `context.RequestAborted`                | Cancellation token    |
| `Stream`              | `context.Request.Body`                  | Raw request body      |
| `PipeReader`          | `context.Request.BodyReader`            | Streaming body reader |
| `IFormFile`           | `context.Request.Form.Files[paramName]` | Single file upload    |
| `IFormFileCollection` | `context.Request.Form.Files`            | Multiple file uploads |
| `IFormCollection`     | `context.Request.Form`                  | Form data             |

**Detection Example:**

```csharp
[Get("/download")]
public static ErrorOr<FileResult> Download(
    int fileId,
    CancellationToken ct) // ← Auto-detected, no attribute needed
```

**Package Dependencies:**

```xml

<PackageReference Include="Microsoft.AspNetCore.Http.Abstractions" Version="2.3.0"/>
<PackageReference Include="System.IO.Pipelines" Version="10.0.1"/>
```

---

### Priority 2: TryParse & BindAsync Detection (30 min)

#### TryParse Method

**Spec:
** [TryParse - Microsoft Learn](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/parameter-binding#tryparse)

**Signatures:**

```csharp
public static bool TryParse(string value, out T result);
public static bool TryParse(string value, IFormatProvider provider, out T result);
```

**Example:**

```csharp
public class Point
{
    public double X { get; set; }
    public double Y { get; set; }

    public static bool TryParse(string? value, IFormatProvider? provider, out Point? point)
    {
        // Format: "12.3,10.1"
        var segments = value?.Split(',');
        if (segments?.Length == 2 
            && double.TryParse(segments[0], out var x)
            && double.TryParse(segments[1], out var y))
        {
            point = new Point { X = x, Y = y };
            return true;
        }
        point = null;
        return false;
    }
}

// Usage: GET /map?point=12.3,10.1
[Get("/map")]
public static ErrorOr<string> GetMap(Point point) 
    => $"Point: {point.X}, {point.Y}";
```

---

#### BindAsync Method

**Spec:
** [BindAsync - Microsoft Learn](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/parameter-binding#bindasync)

**Signatures:**

```csharp
public static ValueTask<T?> BindAsync(HttpContext context, ParameterInfo parameter);
public static ValueTask<T?> BindAsync(HttpContext context);
```

**Example:**

```csharp
public class PagingData
{
    public string? SortBy { get; init; }
    public SortDirection SortDirection { get; init; }
    public int CurrentPage { get; init; } = 1;

    public static ValueTask<PagingData?> BindAsync(HttpContext context, ParameterInfo parameter)
    {
        Enum.TryParse<SortDirection>(context.Request.Query["sortDir"], true, out var sortDir);
        int.TryParse(context.Request.Query["page"], out var page);
        
        return ValueTask.FromResult<PagingData?>(new PagingData
        {
            SortBy = context.Request.Query["sortBy"],
            SortDirection = sortDir,
            CurrentPage = page == 0 ? 1 : page
        });
    }
}

// Usage: GET /products?sortBy=name&sortDir=desc&page=2
[Get("/products")]
public static ErrorOr<Product[]> GetProducts(PagingData pageData) => ...;
```

---

#### IBindableFromHttpContext<T>

**Spec:
** [Custom parameter binding - Microsoft Learn](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/parameter-binding#custom-parameter-binding-with-ibindablefromhttpcontext)

**Interface:**

```csharp
public interface IBindableFromHttpContext<TSelf>
    where TSelf : class, IBindableFromHttpContext<TSelf>
{
    static abstract ValueTask<TSelf?> BindAsync(HttpContext context, ParameterInfo parameter);
}
```

**Example:**

```csharp
public class CustomBoundParameter : IBindableFromHttpContext<CustomBoundParameter>
{
    public string Value { get; init; } = default!;

    public static ValueTask<CustomBoundParameter?> BindAsync(
        HttpContext context, 
        ParameterInfo parameter)
    {
        var value = context.Request.Headers["X-Custom-Header"].ToString();
        
        if (string.IsNullOrEmpty(value))
            value = context.Request.Query["customValue"].ToString();
        
        return ValueTask.FromResult<CustomBoundParameter?>(
            new CustomBoundParameter { Value = value });
    }
}
```

---

### Priority 3: Array/Collection Binding (45 min)

**Spec:
** [Bind arrays and string values - Microsoft Learn](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/parameter-binding#bind-arrays-and-string-values-from-headers-and-query-strings)

**Supported Patterns:**

```csharp
// Primitive arrays from query string
// GET /tags?q=1&q=2&q=3
[Get("/tags")]
public static ErrorOr<string> GetTags(int[] q) 
    => $"tag1: {q[0]}, tag2: {q[1]}, tag3: {q[2]}";

// String arrays
// GET /search?names=john&names=jack&names=jane
[Get("/search")]
public static ErrorOr<User[]> Search(string[] names) => ...;

// StringValues (Microsoft.Extensions.Primitives)
[Get("/filter")]
public static ErrorOr<Item[]> Filter(StringValues categories) => ...;

// Custom types with TryParse
// GET /todos?tags=home&tags=work
[Get("/todos")]
public static ErrorOr<Todo[]> GetTodos(Tag[] tags) => ...;

public class Tag
{
    public string Name { get; set; }
    
    public static bool TryParse(string? name, out Tag tag)
    {
        if (name is null) { tag = default!; return false; }
        tag = new Tag { Name = name };
        return true;
    }
}
```

**From Headers:**

```csharp
// GET /items with header "X-Todo-Id: 1,2,3"
[Get("/items")]
public static ErrorOr<Todo[]> GetItems(
    [FromHeader(Name = "X-Todo-Id")] int[] ids) => ...;
```

---

### Priority 4: AsParameters Recursion (60 min)

**Spec:
** [Parameter binding with AsParameters - Microsoft Learn](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/parameter-binding#parameter-binding-for-argument-lists-with-asparameters)

**Basic Usage:**

```csharp
// Before
[Get("/{id}")]
public static ErrorOr<Todo> GetTodo(int id, TodoDb db) => ...;

// After (cleaner parameter lists)
[Get("/{id}")]
public static ErrorOr<Todo> GetTodo([AsParameters] TodoRequest request) => ...;

record TodoRequest(int Id, TodoDb Db);
```

**With Explicit Attributes:**

```csharp
public record SearchRequest(
    [FromRoute] int CategoryId,
    [FromQuery] string? Search,
    [FromQuery] int Page = 1,
    [FromQuery] int PageSize = 20,
    [FromServices] ISearchService SearchService);

[Get("/categories/{categoryId}/search")]
public static ErrorOr<SearchResult> Search([AsParameters] SearchRequest request) => ...;
```

**With Forms:**

```csharp
public record NewTodoRequest(
    [FromForm] string Name,
    [FromForm] Visibility Visibility, 
    IFormFile? Attachment);

[Post("/todos")]
public static ErrorOr<Todo> CreateTodo([AsParameters] NewTodoRequest request) => ...;
```

---

### Priority 5: Form Binding with Antiforgery (90 min)

**Spec:
** [Binding to forms - Microsoft Learn](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/parameter-binding#binding-to-forms-with-iformcollection-iformfile-and-iformfilecollection)

**Simple Form Upload:**

```csharp
builder.Services.AddAntiforgery();
app.UseAntiforgery();

[Post("/upload")]
public static async Task<ErrorOr<Created>> UploadFile(
    IFormFile file,
    HttpContext context,
    [FromServices] IAntiforgery antiforgery)
{
    await antiforgery.ValidateRequestAsync(context);
    
    var path = Path.Combine("uploads", file.FileName);
    await using var stream = File.Create(path);
    await file.CopyToAsync(stream);
    
    return Result.Created;
}
```

**Complex Form Binding:**

```csharp
[Post("/todos")]
public static async Task<ErrorOr<Todo>> CreateTodo(
    [FromForm] Todo todo,
    HttpContext context,
    [FromServices] IAntiforgery antiforgery)
{
    await antiforgery.ValidateRequestAsync(context);
    
    if (string.IsNullOrWhiteSpace(todo.Name))
        return Error.Validation("Todo.Name", "Name is required");
    
    return todo;
}

class Todo
{
    public string Name { get; set; } = "";
    public bool IsCompleted { get; set; }
    public DateTime DueDate { get; set; }
}
```

**Package Dependency:**

```xml

<PackageReference Include="Microsoft.AspNetCore.Antiforgery" Version="2.3.0"/>
```

---

## Reference Documentation

### Route Primitive Types

**Source:
** [Route constraint reference - Microsoft Learn](https://learn.microsoft.com/aspnet/core/fundamentals/routing#route-constraint-reference)

| Type       | Constraint  | Example           |
|------------|-------------|-------------------|
| `string`   | (default)   | `{slug}`          |
| `int`      | `:int`      | `{id:int}`        |
| `long`     | `:long`     | `{id:long}`       |
| `Guid`     | `:guid`     | `{id:guid}`       |
| `bool`     | `:bool`     | `{flag:bool}`     |
| `DateTime` | `:datetime` | `{date:datetime}` |
| `decimal`  | `:decimal`  | `{price:decimal}` |
| `double`   | `:double`   | `{value:double}`  |
| `float`    | `:float`    | `{value:float}`   |

All other numeric types (`DateOnly`, `TimeOnly`, `TimeSpan`, `byte`, etc.) use `TryParse`.

---

### Error Type Mapping

**Current Implementation** (code analysis):

| ErrorOr Type           | HTTP Status | Response Type                  |
|------------------------|-------------|--------------------------------|
| `Error.Validation()`   | 400         | `HttpValidationProblemDetails` |
| `Error.Unauthorized()` | 401         | `ProblemDetails`               |
| `Error.Forbidden()`    | 403         | `ProblemDetails`               |
| `Error.NotFound()`     | 404         | `ProblemDetails`               |
| `Error.Conflict()`     | 409         | `ProblemDetails`               |
| `Error.Failure()`      | 422         | `ProblemDetails`               |
| `Error.Unexpected()`   | 500         | `ProblemDetails`               |

**Inference Strategy:**
The generator analyzes method bodies and detects `Error.XXX()` calls to automatically add OpenAPI metadata. This is more
accurate than XML docs because it reflects actual code paths.

---

### Success Type Mapping

| ErrorOr Type       | HTTP Status | OpenAPI Response                                |
|--------------------|-------------|-------------------------------------------------|
| `ErrorOr<T>`       | 200         | `ProducesResponseTypeAttribute(typeof(T), 200)` |
| `ErrorOr<Deleted>` | 204         | `ProducesResponseTypeAttribute(204)`            |
| `ErrorOr<Updated>` | 204         | `ProducesResponseTypeAttribute(204)`            |
| `ErrorOr<Created>` | 201         | `ProducesResponseTypeAttribute(201)`            |
| `ErrorOr<Success>` | 204         | `ProducesResponseTypeAttribute(204)`            |
| `ErrorOr<SseItem<T>>`| 200        | `ProducesResponseTypeAttribute(200)` (SSE)      |

---

## Design Decisions

### Why Manual Metadata?

When using raw `RequestDelegate` with `MapMethods`, ASP.NET Core **cannot** infer metadata automatically:

```csharp
// Generated approach (raw RequestDelegate)
app.MapMethods(@"/{id}", new[] { "GET" }, (RequestDelegate)Invoke_Ep2)
    .WithMetadata(...) // ← MUST add manually

// vs. Standard Minimal API (typed delegate)
app.MapGet("/users/{id}", (int id) => TypedResults.Ok(user))
    .Produces<User>(200)  // ← Can infer from TypedResults.Ok<User>
```

**Solution:** We infer metadata from:

1. `ErrorOr<T>` return type → success response
2. Method body analysis → error responses
3. Route template → parameter metadata

---

### Why Explicit `[FromServices]`?

| Approach                               | Pros                                   | Cons                                       |
|----------------------------------------|----------------------------------------|--------------------------------------------|
| **Microsoft Default** (implicit DI)    | Familiar to developers                 | Runtime failures if service not registered |
| **ErrorOr.Http** (explicit attributes) | Compile-time safety, no runtime checks | More verbose                               |

**Decision:** Explicit attributes prevent runtime DI crashes in AOT scenarios and make dependencies visible in code.

**Trade-off:** Developers write `[FromServices]` but get compile-time errors instead of runtime surprises.

---

### Why No XML Documentation (v1.0)?

**Current:** Error inference from code analysis  
**Future:** Leverages native .NET 10 XML doc support for OpenAPI descriptions.

**Reasoning:**

- Code analysis is more accurate (reflects actual implementation)
- XML docs can lie; code can't
- Keeps v1.0 implementation simple
- **Note:** .NET 10 introduces AOT-compatible XML doc support and `builder.Services.AddValidation()`, making custom implementations for these redundant.

**Example:**

```csharp
// This is detected automatically:
public static ErrorOr<User> GetUser(int id) => id switch
{
    < 0 => Error.Validation(...),  // → 400 in OpenAPI
    0 => Error.NotFound(...),      // → 404 in OpenAPI
    _ => new User(id)
};

// This would require XML parsing (v2.0):
/// <response code="429">Rate limited</response>
/// <response code="503">Service unavailable</response>
```

---

## Package Dependencies

### Source Generator Project

```xml

<ItemGroup>
    <!-- Type definitions for symbol analysis (not runtime) -->
    <PackageReference Include="Microsoft.AspNetCore.Http.Abstractions" Version="2.3.0"/>
    <PackageReference Include="Microsoft.AspNetCore.Antiforgery" Version="2.3.0"/>

    <!-- Current version for pipeline types -->
    <PackageReference Include="System.IO.Pipelines" Version="10.0.1"/>
</ItemGroup>
```

**Why version 2.3.0?**

- Lowest version with stable APIs we need
- Avoids forcing users to upgrade
- Generator analyzes **symbols**, not runtime behavior

### Consumer Project

```xml
<!-- Users get runtime packages automatically from .NET 10 SDK -->
<Project Sdk="Microsoft.NET.Sdk.Web">
    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="ErrorOr.Http" Version="1.0.0"/>
    </ItemGroup>
</Project>
```

---

## Implementation Checklist

### v1.0 (Current)

- [x] Route parameter binding
- [x] Query parameter binding (primitives & collections)
- [x] Header binding
- [x] Body binding (JSON)
- [x] Service injection
- [x] Keyed Service injection
- [x] Error → ProblemDetails mapping
- [x] OpenAPI metadata generation
- [x] Async handlers
- [x] Obsolete attribute propagation
- [x] NativeAOT support
- [x] Implicit Query (primitives)
- [x] Safety Check (No implicit DI)

### v2.0 (Planned)

- [x] **Priority 4:** AsParameters recursion
- [x] **Priority 5:** Form binding + antiforgery
- [ ] **Priority 6:** SSE / Streaming Support

---
