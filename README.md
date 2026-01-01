# ErrorOr.Http

[![NuGet](https://img.shields.io/nuget/v/ErrorOr.Http?label=NuGet&color=0891B2)](https://www.nuget.org/packages/ErrorOr.Http/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/ANcpLua/ErrorOr.Http/blob/main/LICENSE)

> **Source generator for ASP.NET Core Minimal APIs that return `ErrorOr<T>`**

Automatically generates type-safe endpoint mappings with ProblemDetails error handling and OpenAPI metadata. No runtime
reflection, fully AOT-compatible.

---

## Features

✅ **Attribute-Driven** — Annotate static methods with `[Get]`, `[Post]`, etc.  
✅ **Type-Safe Binding** — Compile-time errors instead of runtime surprises  
✅ **ProblemDetails** — Automatic error mapping from `ErrorOr<T>`  
✅ **OpenAPI Metadata** — Inferred from code + Native .NET 10 XML Doc Support  
✅ **OpenAPI 3.1** — Default schema generation  
✅ **Built-in Validation** — Leverages .NET 10 `builder.Services.AddValidation()`  
✅ **NativeAOT Ready** — Generated JSON serialization context  
✅ **.NET 10 Compatible** — Returns 401/403 instead of redirects  
✅ **Streaming** — Support for `ErrorOr<SseItem<T>>` (v2.0)

---

## Quick Start

### 1. Install

```bash
dotnet add package ErrorOr.Http
```

### 2. Annotate Handlers

```csharp
using ErrorOr.Http;

public static class TodoEndpoints
{
    [Get("/todos")]
    public static ErrorOr<Todo[]> GetAll() =>
        Database.Todos;

    [Get("/todos/{id:int}")]
    public static ErrorOr<Todo> GetById(int id) =>
        Database.Todos.Find(id) 
            ?? Error.NotFound("Todo.NotFound", $"Todo {id} not found");

    [Post("/todos")]
    public static ErrorOr<Created> Create([FromBody] Todo todo)
    {
        if (string.IsNullOrWhiteSpace(todo.Title))
            return Error.Validation("Todo.Title", "Title required");
            
        Database.Todos.Add(todo);
        return Result.Created;
    }

    [Delete("/todos/{id:int}")]
    public static ErrorOr<Deleted> Delete(int id) =>
        Database.Todos.Delete(id) 
            ? Result.Deleted 
            : Error.NotFound();
}
```

### 3. Map Endpoints

```csharp
using ErrorOr.Http.Generated;

var app = builder.Build();

app.MapErrorOr<TodoEndpoints>();
// or with group prefix:
// app.MapGroup("/api").MapErrorOr<TodoEndpoints>();

app.Run();
```

**That's it.** All endpoints are mapped with error handling and OpenAPI metadata.

---

## Parameter Binding

### Explicit Contracts

ErrorOr.Http requires **explicit binding attributes** to prevent runtime errors:

```csharp
[Get("/users/{id}")]
public static ErrorOr<User> GetUser(
    int id,                                      // ← Route (matches {id})
    [FromQuery] string? search,                  // ← Query string
    [FromHeader(Name = "X-Api-Version")] int v,  // ← Header
    [FromServices] IUserService service)         // ← Dependency injection
```

**Why explicit?** Compile-time safety. If binding is ambiguous, you get a **build error** (EOE004) instead of a runtime
crash.

---

### Binding Rules

| Source             | Attribute                    | Example              |
|--------------------|------------------------------|----------------------|
| **Route**          | (auto-detected)              | `int id` from `{id}` |
| **Query**          | `[FromQuery]`                | `?search=abc`        |
| **Header**         | `[FromHeader]`               | `X-Api-Version: 2`   |
| **Body**           | `[FromBody]`                 | JSON request body    |
| **Services**       | `[FromServices]`             | DI container         |
| **Keyed Services** | `[FromKeyedServices("key")]` | DI with key          |

---

### Supported Types (v1.0)

✅ **Primitives:** `int`, `string`, `Guid`, `DateTime`, etc.  
✅ **Nullables:** `int?`, `string?`  
✅ **Arrays:** `int[]`, `string[]` (from query: `?ids=1&ids=2`)  
✅ **DTOs:** Complex types from JSON body  
✅ **Special:** `HttpContext`, `CancellationToken`

⚠️ **Coming in v2.0:**

- Custom types with `TryParse`/`BindAsync`
- `[AsParameters]` for grouped parameters
- Form uploads (`IFormFile`, `[FromForm]`)
- Stream/PipeReader body binding

---

## Error Handling

ErrorOr types automatically map to HTTP status codes:

| ErrorOr Type           | HTTP Status | Response                       |
|------------------------|-------------|--------------------------------|
| `Error.Validation()`   | 400         | `HttpValidationProblemDetails` |
| `Error.Unauthorized()` | 401         | `ProblemDetails`               |
| `Error.Forbidden()`    | 403         | `ProblemDetails`               |
| `Error.NotFound()`     | 404         | `ProblemDetails`               |
| `Error.Conflict()`     | 409         | `ProblemDetails`               |
| `Error.Failure()`      | 422         | `ProblemDetails`               |
| `Error.Unexpected()`   | 500         | `ProblemDetails`               |

**Example:**

```csharp
[Get("/users/{id}")]
public static ErrorOr<User> GetUser(int id)
{
    if (id <= 0)
        return Error.Validation("User.Id", "ID must be positive");
    
    if (Database.Users.Find(id) is not User user)
        return Error.NotFound("User.NotFound", $"User {id} not found");
    
    if (!user.IsActive)
        return Error.Forbidden("User.Inactive", "User inactive");
    
    return user;
}
```

**Generated OpenAPI:**

```yaml
responses:
  200:
    description: Success
    content:
      application/json:
        schema:
          $ref: '#/components/schemas/User'
  400:
    description: Validation error
  403:
    description: Forbidden
  404:
    description: Not found
```

---

## Advanced Features

### Async Handlers

```csharp
[Get("/users/{id}")]
public static async Task<ErrorOr<User>> GetUser(
    int id,
    [FromServices] IUserRepository repo,
    CancellationToken ct)
{
    var user = await repo.GetByIdAsync(id, ct);
    return user ?? Error.NotFound();
}
```

Also supports `ValueTask<ErrorOr<T>>`.

---

### Multiple Routes

```csharp
[Get("/users/{id:int}")]
[Get("/users/by-email/{email}")]
public static ErrorOr<User> GetUser(int? id = null, string? email = null)
{
    if (id is not null)
        return Database.Users.Find(id.Value);
    
    if (email is not null)
        return Database.Users.FindByEmail(email);
    
    return Error.Validation("User", "Provide id or email");
}
```

---

### Keyed Services (.NET 8+)

```csharp
builder.Services.AddKeyedScoped<ICache>("redis", sp => new RedisCache());
builder.Services.AddKeyedScoped<ICache>("memory", sp => new MemoryCache());

[Get("/data/{key}")]
public static ErrorOr<string> GetData(
    string key,
    [FromKeyedServices("redis")] ICache cache)
    => cache.Get(key) ?? Error.NotFound();
```

---

### Obsolete Endpoints

```csharp
[Obsolete("Use GetUserV2 instead", error: false)]
[Get("/users/{id}")]
public static ErrorOr<User> GetUser(int id) => ...;
```

Automatically adds `[Obsolete]` metadata to OpenAPI.

---

## NativeAOT Support

For NativeAOT publishing, JSON serialization requires source generation:

### 1. Build your project

The generator creates `ErrorOrJsonContext.suggested.cs` in `obj/Generated/`.

### 2. Create `ErrorOrJsonContext.cs`

Copy the template and remove `#if`/`#endif` markers:

```csharp
using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Todo))]
[JsonSerializable(typeof(Todo[]))]
[JsonSerializable(typeof(User))]
[JsonSerializable(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails))]
[JsonSerializable(typeof(Microsoft.AspNetCore.Http.HttpValidationProblemDetails))]
internal partial class ErrorOrJsonContext : JsonSerializerContext
{
}
```

### 3. Register in `Program.cs`

```csharp
builder.Services.AddErrorOrJson<ErrorOrJsonContext>();
app.MapErrorOr<TodoEndpoints>();
```

**Done.** Your app is now fully AOT-compatible.

---

## .NET 10 Authentication

Starting with .NET 10, endpoints created by ErrorOr.Http automatically return `401 Unauthorized` or `403 Forbidden`
instead of redirecting to login pages.

**Server-side — no changes needed:**

```csharp
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie();
    
app.UseAuthentication();
app.UseAuthorization();

app.MapErrorOr<UserEndpoints>();
```

**Client-side — handle status codes:**

```typescript
fetch('/api/users/1')
  .then(res => {
    if (res.status === 401) {
      window.location.href = '/login';
      return;
    }
    return res.json();
  })
```

**Reference:
** [API endpoint authentication behavior](https://learn.microsoft.com/aspnet/core/security/authentication/cookie#api-endpoint-authentication-behavior)

---

## Requirements

- **Framework:** .NET 10.0 or later
- **Handlers:** Must be `static` methods
- **Return Type:** `ErrorOr<T>`, `Task<ErrorOr<T>>`, or `ValueTask<ErrorOr<T>>`

---

## Roadmap

### v1.0 (Current — Stable)

✅ Route/query/header/body binding  
✅ Service injection  
✅ Error → ProblemDetails mapping  
✅ OpenAPI metadata generation  
✅ Async handlers  
✅ NativeAOT support

### v2.0 (Planned)

🚧 Custom binding (`TryParse`, `BindAsync`)  
🚧 `[AsParameters]` for grouped parameters  
🚧 Form uploads (`IFormFile`, `[FromForm]`)  
🚧 XML documentation → OpenAPI descriptions  
🚧 Data annotation validation  
🚧 Stream/PipeReader body binding

---

## Examples

### Full CRUD API

```csharp
using ErrorOr.Http;

public static class ProductEndpoints
{
    [Get("/products")]
    public static ErrorOr<Product[]> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
        => Database.Products
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArray();

    [Get("/products/{id:int}")]
    public static ErrorOr<Product> GetById(int id)
        => Database.Products.Find(id) 
            ?? Error.NotFound();

    [Post("/products")]
    public static ErrorOr<Created> Create([FromBody] Product product)
    {
        if (string.IsNullOrWhiteSpace(product.Name))
            return Error.Validation("Product.Name", "Name required");
        
        Database.Products.Add(product);
        return Result.Created;
    }

    [Put("/products/{id:int}")]
    public static ErrorOr<Updated> Update(int id, [FromBody] Product product)
    {
        if (!Database.Products.Update(id, product))
            return Error.NotFound();
        
        return Result.Updated;
    }

    [Delete("/products/{id:int}")]
    public static ErrorOr<Deleted> Delete(int id)
        => Database.Products.Delete(id) 
            ? Result.Deleted 
            : Error.NotFound();
}
```

---

## Comparison

### Before (Manual Minimal API)

```csharp
app.MapGet("/users/{id}", async (int id, IUserService service) =>
{
    var result = await service.GetByIdAsync(id);
    
    if (result.IsError)
    {
        var error = result.FirstError;
        var status = error.Type switch
        {
            ErrorType.NotFound => 404,
            ErrorType.Validation => 400,
            _ => 500
        };
        return Results.Problem(error.Description, statusCode: status);
    }
    
    return Results.Ok(result.Value);
})
.WithName("GetUser")
.Produces<User>(200)
.ProducesProblem(404)
.ProducesProblem(400);
```

### After (ErrorOr.Http)

```csharp
public static class UserEndpoints
{
    [Get("/users/{id:int}")]
    public static async Task<ErrorOr<User>> GetUser(
        int id,
        [FromServices] IUserService service)
        => await service.GetByIdAsync(id);
}

app.MapErrorOr<UserEndpoints>();
```

**Lines of code:** 20 → 8  
**Type safety:** Manual → Compile-time  
**Error handling:** Manual → Automatic  
**OpenAPI:** Manual → Inferred

---

## FAQ

### Q: Why require explicit `[FromServices]`?

**A:** Compile-time safety. Microsoft's default behavior tries implicit DI, which fails at runtime if the service isn't
registered. We emit a build error instead.

### Q: Can I use this with Controllers?

**A:** No, ErrorOr.Http is designed for Minimal APIs. For Controllers, use the standard `[ApiController]` approach.

### Q: Does this work with Swagger/OpenAPI?

**A:** Yes, all metadata is automatically added via `ProducesResponseTypeAttribute`.

### Q: What about rate limiting / CORS / authorization?

**A:** Use standard ASP.NET Core middleware:

```csharp
app.UseRateLimiter();
app.UseCors();
app.UseAuthorization();

app.MapErrorOr<UserEndpoints>()
    .RequireAuthorization()
    .RequireRateLimiting("fixed");
```

---

## Contributing

Contributions welcome! Please see [CONTRIBUTING.md](CONTRIBUTING.md).

---

## License

[MIT](LICENSE) © Alexander Nachtmann

---

## Links

- [GitHub Repository](https://github.com/ANcpLua/ErrorOr.Http)
- [NuGet Package](https://www.nuget.org/packages/ErrorOr.Http/)
- [ErrorOr Library](https://github.com/amantinband/error-or)
- [ASP.NET Core Minimal APIs](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis)
