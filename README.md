[![NuGet](https://img.shields.io/nuget/v/ErrorOr.Interceptors?label=NuGet&color=0891B2)](https://www.nuget.org/packages/ErrorOr.Interceptors/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/ANcpLua/ErrorOr.Interceptors/blob/main/LICENSE)

# ErrorOr.Interceptors

Source generator that automatically adds **ProblemDetails mapping** and **OpenAPI metadata** to Minimal API endpoints returning `ErrorOr<T>`.

**Zero attributes. Zero reflection. Zero boilerplate.**

## Install

```bash
dotnet add package ErrorOr.Interceptors
```

## What It Does

Write normal Minimal API code with [ErrorOr](https://github.com/amantinband/error-or):

```csharp
app.MapGet("/users/{id}", (int id, UserService service) =>
{
    return service.GetUser(id);  // Returns ErrorOr<User>
});
```

The generator intercepts this at compile time and adds:
- `.AddEndpointFilter()` - converts `ErrorOr` errors to `ProblemDetails`
- `.Produces<User>(200)` - OpenAPI success response
- `.ProducesProblem(404)` - OpenAPI error responses (inferred from your code)

## Examples

### GET - Return a resource

```csharp
app.MapGet("/users/{id}", (int id, UserService service) =>
{
    if (id <= 0)
        return Error.Validation("User.InvalidId", "ID must be positive");

    var user = service.FindById(id);
    if (user is null)
        return Error.NotFound("User.NotFound", $"User {id} not found");

    return user;  // Implicitly converts to ErrorOr<User>
});
```

### POST - Create a resource

```csharp
app.MapPost("/users", (CreateUserRequest request, UserService service) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
        return Error.Validation("User.NameRequired", "Name is required");

    if (service.ExistsByEmail(request.Email))
        return Error.Conflict("User.EmailExists", "Email already registered");

    var user = service.Create(request);
    return user;  // Returns 201 Created
});
```

### DELETE - No content response

```csharp
app.MapDelete("/users/{id}", (int id, UserService service) =>
{
    if (!service.Exists(id))
        return Error.NotFound("User.NotFound", $"User {id} not found");

    service.Delete(id);
    return Result.Deleted;  // Returns 204 No Content
});
```

### Async handlers

```csharp
app.MapGet("/orders/{id}", async (int id, OrderService service, CancellationToken ct) =>
{
    var order = await service.GetByIdAsync(id, ct);
    if (order is null)
        return Error.NotFound("Order.NotFound", $"Order {id} not found");

    return order;
});
```

## Error Mapping

| ErrorOr Type | HTTP Status | Response |
|--------------|-------------|----------|
| `Error.Validation()` | 400 | `ValidationProblemDetails` |
| `Error.Unauthorized()` | 401 | `ProblemDetails` |
| `Error.Forbidden()` | 403 | `ProblemDetails` |
| `Error.NotFound()` | 404 | `ProblemDetails` |
| `Error.Conflict()` | 409 | `ProblemDetails` |
| `Error.Failure()` | 422 | `ProblemDetails` |
| `Error.Unexpected()` | 500 | `ProblemDetails` |

## Performance

This package generates typed code at compile time. No reflection at runtime.

| Scenario | This Package | Reflection-based | Speedup |
|----------|-------------|------------------|---------|
| Success response | 7 ns | 560 ns | **80x faster** |
| Created response | 7 ns | 617 ns | **88x faster** |
| NoContent response | 3 ns | 15 ns | **5x faster** |
| Error response | 25 ns | 39 ns | **1.5x faster** |

<details>
<summary>Raw benchmark data (.NET 10, Apple M4)</summary>

```
| Method                                | Mean       | Allocated |
|-------------------------------------- |-----------:|----------:|
| Generated: Success -> Ok<User>        |   7.114 ns |      48 B |
| Reflection: Success -> Ok<User>       | 559.873 ns |    1528 B |
| Generated: Success -> Created<User>   |   6.901 ns |      56 B |
| Reflection: Success -> Created<User>  | 617.208 ns |    1520 B |
| Generated: Deleted -> NoContent       |   2.922 ns |      24 B |
| Reflection: Deleted -> NoContent      |  14.857 ns |      24 B |
| Generated: MultiError -> Problem      |  25.004 ns |     192 B |
| Reflection: MultiError -> Problem     |  38.593 ns |     176 B |
```

</details>

## License

[MIT](LICENSE)
