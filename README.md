# ErrorOr.Http

[![NuGet](https://img.shields.io/nuget/v/ErrorOr.Http?label=NuGet&color=0891B2)](https://www.nuget.org/packages/ErrorOr.Http/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Source generator for ASP.NET Core Minimal APIs that transforms `ErrorOr<T>` handlers into type-safe endpoints with
automatic ProblemDetails responses and OpenAPI metadata.

## Features

- **Attribute-driven routing** with `[Get]`, `[Post]`, `[Put]`, `[Delete]`, `[Patch]`
- **Compile-time parameter binding** validation prevents runtime errors
- **Automatic error mapping** from ErrorOr types to RFC 7807 ProblemDetails
- **OpenAPI metadata** inferred from return types and error paths
- **NativeAOT compatible** with generated JSON serialization contexts
- **Streaming support** for `IAsyncEnumerable<T>` via Server-Sent Events

## Quick Start

```bash
dotnet add package ErrorOr.Http
```

```csharp
using ErrorOr.Http;

public static class TodoEndpoints
{
    [Get("/todos")]
    public static ErrorOr<Todo[]> GetAll() => Database.Todos;

    [Get("/todos/{id}")]
    public static ErrorOr<Todo> GetById(int id) =>
        Database.Find(id) ?? Error.NotFound();

    [Post("/todos")]
    public static ErrorOr<Created> Create([FromBody] Todo todo)
    {
        Database.Add(todo);
        return Result.Created;
    }
}
```

```csharp
using ErrorOr.Http.Generated;

var app = builder.Build();
app.MapErrorOrEndpoints();
app.Run();
```

## Usage

### HTTP Method Attributes

```csharp
[Get("/resources")]
public static ErrorOr<Resource[]> List() => ...;

[Get("/resources/{id}")]
public static ErrorOr<Resource> Get(int id) => ...;

[Post("/resources")]
public static ErrorOr<Created> Create([FromBody] Resource r) => ...;

[Put("/resources/{id}")]
public static ErrorOr<Updated> Update(int id, [FromBody] Resource r) => ...;

[Patch("/resources/{id}")]
public static ErrorOr<Updated> Patch(int id, [FromBody] ResourcePatch p) => ...;

[Delete("/resources/{id}")]
public static ErrorOr<Deleted> Delete(int id) => ...;
```

For non-standard HTTP methods:

```csharp
[ErrorOrEndpoint("OPTIONS", "/resources")]
public static ErrorOr<string[]> Options() => new[] { "GET", "POST" };

[ErrorOrEndpoint("HEAD", "/resources/{id}")]
public static ErrorOr<Success> Head(int id) => Result.Success;
```

### Parameter Binding

Route parameters are inferred from the template. All other sources require explicit attributes.

```csharp
[Get("/users/{id}")]
public static ErrorOr<User> GetUser(
    int id,                                       // Route (matches {id})
    [FromQuery] string? search,                   // Query string
    [FromHeader(Name = "X-Api-Version")] int v,   // Header
    [FromServices] IUserService service)          // DI container
    => ...;
```

### Body Binding

```csharp
[Post("/users")]
public static ErrorOr<Created> CreateUser([FromBody] CreateUserRequest request)
    => ...;
```

### Form Binding

```csharp
[Post("/upload")]
public static ErrorOr<Created> Upload(
    [FromForm] string title,
    [FromForm] int version,
    IFormFile document)
    => ...;

[Post("/upload-multiple")]
public static ErrorOr<Created> UploadMany(
    [FromForm] UploadRequest request,
    IFormFileCollection attachments)
    => ...;

public record UploadRequest(string Title, string Description);
```

### Grouped Parameters

```csharp
[Get("/search")]
public static ErrorOr<SearchResult> Search([AsParameters] SearchRequest request)
    => ...;

public record SearchRequest(
    [FromQuery] string Query,
    [FromQuery] int Page = 1,
    [FromHeader(Name = "X-Api-Key")] string ApiKey,
    [FromServices] ISearchService Service);
```

### Async Handlers

```csharp
[Get("/users/{id}")]
public static async Task<ErrorOr<User>> GetUser(
    int id,
    [FromServices] IUserRepository repo,
    CancellationToken ct)
    => await repo.GetByIdAsync(id, ct) ?? Error.NotFound();
```

### Streaming (Server-Sent Events)

```csharp
[Get("/events")]
public static ErrorOr<IAsyncEnumerable<Event>> StreamEvents()
{
    return GetEvents();

    static async IAsyncEnumerable<Event> GetEvents()
    {
        while (true)
        {
            yield return await GetNextEvent();
        }
    }
}
```

### Multiple Routes

```csharp
[Get("/users/{id}")]
[Get("/users/by-email/{email}")]
public static ErrorOr<User> GetUser(int? id = null, string? email = null)
    => ...;
```

## Error Handling

ErrorOr types map to HTTP status codes automatically:

| ErrorOr Type           | HTTP Status | Response Type                  |
|------------------------|-------------|--------------------------------|
| `Error.Validation()`   | 400         | `HttpValidationProblemDetails` |
| `Error.Unauthorized()` | 401         | `ProblemDetails`               |
| `Error.Forbidden()`    | 403         | `ProblemDetails`               |
| `Error.NotFound()`     | 404         | `ProblemDetails`               |
| `Error.Conflict()`     | 409         | `ProblemDetails`               |
| `Error.Failure()`      | 422         | `ProblemDetails`               |
| `Error.Unexpected()`   | 500         | `ProblemDetails`               |

Success types:

| ErrorOr Type       | HTTP Status |
|--------------------|-------------|
| `ErrorOr<T>`       | 200         |
| `ErrorOr<Created>` | 201         |
| `ErrorOr<Success>` | 204         |
| `ErrorOr<Updated>` | 204         |
| `ErrorOr<Deleted>` | 204         |

## NativeAOT Support

For AOT publishing, register a JSON serialization context:

```csharp
[JsonSerializable(typeof(Todo))]
[JsonSerializable(typeof(Todo[]))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
internal partial class AppJsonContext : JsonSerializerContext { }
```

```csharp
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolver = AppJsonContext.Default;
});
```

The generator creates `ErrorOrJsonContext.suggested.cs` in `obj/Generated/` with all required types.

## Diagnostics

| Code   | Severity | Description                                                |
|--------|----------|------------------------------------------------------------|
| EOE003 | Error    | Unsupported parameter type                                 |
| EOE004 | Error    | Ambiguous parameter requires explicit binding attribute    |
| EOE005 | Error    | Multiple `[FromBody]` parameters                           |
| EOE006 | Error    | Multiple body sources (`[FromBody]`, `[FromForm]`, Stream) |
| EOE007 | Error    | Multiple `[FromForm]` DTO parameters                       |
| EOE008 | Error    | Unsupported `[FromForm]` DTO shape                         |
| EOE009 | Warning  | Non-nullable `IFormFile` may be missing at runtime         |
| EOE010 | Info     | Form endpoint may receive non-form requests                |
| EOE011 | Error    | Multiple `[FromForm]` complex type parameters              |
| EOE013 | Error    | `IFormCollection` requires explicit `[FromForm]`           |
| EOE014 | Error    | Type cannot be form-bound                                  |
| EOE021 | Warning  | Error type not documented in OpenAPI metadata              |
| EOE022 | Warning  | Type not registered in `JsonSerializerContext`             |

## Requirements

- .NET 10.0 or later
- Handlers must be `static` methods
- Return type must be `ErrorOr<T>`, `Task<ErrorOr<T>>`, or `ValueTask<ErrorOr<T>>`

## License

[MIT](LICENSE)
