[![NuGet](https://img.shields.io/nuget/v/ErrorOr.Interceptors?label=NuGet&color=0891B2)](https://www.nuget.org/packages/ErrorOr.Interceptors/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/ANcpLua/ErrorOr.Interceptors/blob/main/LICENSE)

# ErrorOr.Interceptors

Source generator that automatically adds **ProblemDetails** and **OpenAPI metadata** to Minimal API endpoints returning `ErrorOr<T>`.

## Install

```bash
dotnet add package ErrorOr.Interceptors
```

## Why?

**Without this package** - mixing HTTP concerns into business logic:

```csharp
app.MapGet("/users/{id}", (int id, UserService service) =>
{
    if (id <= 0)
        return Results.ValidationProblem(
            new Dictionary<string, string[]> { ["Id"] = ["Must be positive"] });

    var user = service.FindById(id);
    if (user is null)
        return Results.Problem("Not found", statusCode: 404);

    return Results.Ok(user);
});
```

**With this package** - clean separation:

```csharp
// Endpoint - just calls service
app.MapGet("/users/{id}", (int id, UserService service) => service.GetById(id));

// Service - business logic only, no HTTP knowledge
public ErrorOr<User> GetById(int id)
{
    if (id <= 0)
        return Error.Validation("User.InvalidId", "Must be positive");

    var user = _db.Find(id);
    if (user is null)
        return Error.NotFound("User.NotFound", "Not found");

    return user;
}
```

The generator handles all the HTTP plumbing automatically.

## Full Example

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<UserService>();

var app = builder.Build();

// Clean one-liner endpoints
app.MapGet("/users/{id}", (int id, UserService s) => s.GetById(id));
app.MapPost("/users", (CreateUserRequest req, UserService s) => s.Create(req));
app.MapDelete("/users/{id}", (int id, UserService s) => s.Delete(id));

app.Run();

// Service with business logic
public class UserService
{
    public ErrorOr<User> GetById(int id)
    {
        if (id <= 0)
            return Error.Validation("User.InvalidId", "ID must be positive");

        var user = _db.Find(id);
        return user ?? Error.NotFound("User.NotFound", $"User {id} not found");
    }

    public ErrorOr<User> Create(CreateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Error.Validation("User.NameRequired", "Name is required");

        if (_db.ExistsByEmail(request.Email))
            return Error.Conflict("User.EmailExists", "Email already registered");

        return _db.Insert(new User(request.Name, request.Email));
    }

    public ErrorOr<Deleted> Delete(int id)
    {
        if (!_db.Exists(id))
            return Error.NotFound("User.NotFound", $"User {id} not found");

        _db.Delete(id);
        return Result.Deleted;  // Returns 204 No Content
    }
}
```

## Error Mapping

| ErrorOr | HTTP | Response |
|---------|------|----------|
| `Error.Validation()` | 400 | `ValidationProblemDetails` |
| `Error.Unauthorized()` | 401 | `ProblemDetails` |
| `Error.Forbidden()` | 403 | `ProblemDetails` |
| `Error.NotFound()` | 404 | `ProblemDetails` |
| `Error.Conflict()` | 409 | `ProblemDetails` |
| `Error.Failure()` | 422 | `ProblemDetails` |
| `Error.Unexpected()` | 500 | `ProblemDetails` |

## What Gets Generated

For each endpoint returning `ErrorOr<T>`, the generator adds:

- `.AddEndpointFilter()` - converts errors to `ProblemDetails` at runtime
- `.Produces<T>(200)` - OpenAPI success response
- `.ProducesProblem(4xx)` - OpenAPI error responses (inferred from your code)

## Performance

Compile-time generation. No reflection at runtime.

| Scenario | This Package | Reflection | Speedup |
|----------|-------------|------------|---------|
| Success | 7 ns | 560 ns | **80x** |
| Created | 7 ns | 617 ns | **88x** |
| NoContent | 3 ns | 15 ns | **5x** |
| Error | 25 ns | 39 ns | **1.5x** |

## License

[MIT](LICENSE)
