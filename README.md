[![NuGet](https://img.shields.io/nuget/v/ErrorOr.Interceptors?label=NuGet&color=0891B2)](https://www.nuget.org/packages/ErrorOr.Interceptors/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/ANcpLua/ErrorOr.Interceptors/blob/main/LICENSE)

[![Star this repo](https://img.shields.io/github/stars/ANcpLua/ErrorOr.Interceptors?style=social)](https://github.com/ANcpLua/ErrorOr.Interceptors/stargazers)

**Star if this works for you** - helps others find it.

# ErrorOr.Interceptors

Roslyn source generator for [ErrorOr](https://github.com/amantinband/error-or) + ASP.NET Core Minimal APIs.

**Zero attributes. Zero reflection. Auto-intercepts `MapGet/Post/Put/Delete/Patch` calls returning `ErrorOr<T>`.**

## Install

```bash
dotnet add package ErrorOr.Interceptors
```

## Usage

Just write normal Minimal API code:

```csharp
var app = WebApplication.Create();

app.MapGet("/users/{id}", ErrorOr<User> (int id) => id switch
{
    <= 0 => Error.Validation("User.InvalidId", "ID must be positive"),
    404 => Error.NotFound("User.NotFound", "User not found"),
    _ => new User(id, $"User {id}")
});

app.MapPost("/users", ErrorOr<User> (CreateUserRequest req) =>
    string.IsNullOrWhiteSpace(req.Name)
        ? Error.Validation("Name.Required", "Name required")
        : new User(1, req.Name));

app.MapDelete("/users/{id}", ErrorOr<Deleted> (int id) =>
    id <= 0 ? Error.Validation("InvalidId", "Bad ID") : Result.Deleted);

app.Run();
```

The generator automatically:
1. Intercepts each `MapGet/Post/Put/Delete/Patch` call returning `ErrorOr<T>`
2. Adds `.AddEndpointFilter()` for runtime error-to-ProblemDetails mapping
3. Adds `.Produces<T>()` and `.ProducesProblem()` for OpenAPI metadata

## Async Support

```csharp
app.MapGet("/users/{id}/orders", async Task<ErrorOr<List<Order>>> (int id, CancellationToken ct) =>
{
    var orders = await db.GetOrdersAsync(id, ct);
    return id <= 0 ? Error.Validation("Bad", "ID") : orders;
});
```

`Task<ErrorOr<T>>` and `ValueTask<ErrorOr<T>>` supported.

## Error Mapping

| ErrorType | HTTP Status |
|-----------|-------------|
| Validation | 400 |
| Unauthorized | 401 |
| Forbidden | 403 |
| NotFound | 404 |
| Conflict | 409 |
| Failure | 422 |
| Unexpected | 500 |

## Benchmarks

.NET 10, Apple M4

| Method | Mean | Allocated |
|--------|------|-----------|
| Generated: Success | 7 ns | 48 B |
| Generated: Created | 7 ns | 56 B |
| Generated: Deleted | 3 ns | 24 B |
| Generated: MultiError | 25 ns | 192 B |

<details>
<summary>Full results vs reflection</summary>

```
| Method                                | Mean       | Allocated |
|-------------------------------------- |-----------:|----------:|
| 'Generated: Success -> Ok<User>'      |   7.114 ns |      48 B |
| 'Reflection: Success -> Ok<User>'     | 559.873 ns |    1528 B |
| 'Generated: Success -> Created<User>' |   6.901 ns |      56 B |
| 'Reflection: Success -> Created<User>'| 617.208 ns |    1520 B |
| 'Generated: Deleted -> NoContent'     |   2.922 ns |      24 B |
| 'Reflection: Deleted -> NoContent'    |  14.857 ns |      24 B |
| 'Generated: MultiError -> Problem'    |  25.004 ns |     192 B |
| 'Reflection: MultiError -> Problem'   |  38.593 ns |     176 B |
```

</details>

## License

This project is licensed under the [MIT License](LICENSE).
