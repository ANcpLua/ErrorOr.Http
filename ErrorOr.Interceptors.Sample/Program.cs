// ═══════════════════════════════════════════════════════════════════════════════
// INTERCEPTOR STYLE - Zero attributes! Just standard Minimal API with ErrorOr
// ═══════════════════════════════════════════════════════════════════════════════

using ErrorOr;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();

var app = builder.Build();
app.MapOpenApi();

// ═══════════════════════════════════════════════════════════════════════════════
// Standard Minimal API - The interceptor auto-wraps with TypedResults + OpenAPI
// ═══════════════════════════════════════════════════════════════════════════════

// GET - returns Ok<User> or ProblemDetails
app.MapGet("/users/{id}", ErrorOr<User> (int id) =>
{
    if (id <= 0)
        return Error.Validation("User.InvalidId", "User ID must be positive");
    if (id == 404)
        return Error.NotFound("User.NotFound", $"User {id} not found");
    return new User(id, $"User {id}", $"user{id}@example.com");
});

// POST - returns Created<User> or ProblemDetails
app.MapPost("/users", ErrorOr<User> ([FromBody] CreateUserRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
        return Error.Validation("User.Name.Required", "Name is required");

    if (request.Email == "exists@example.com")
        return Error.Conflict("User.EmailExists", "Email already exists");

    return new User(Random.Shared.Next(1000, 9999), request.Name, request.Email);
});

// DELETE - returns NoContent or ProblemDetails
app.MapDelete("/users/{id}", ErrorOr<Deleted> (int id) =>
{
    if (id <= 0)
        return Error.Validation("User.InvalidId", "Invalid ID");
    if (id == 404)
        return Error.NotFound("User.NotFound", $"User {id} not found");
    return Result.Deleted;
});

// Async endpoint
app.MapGet("/users/{id}/orders", async Task<ErrorOr<List<Order>>> (int id, CancellationToken ct) =>
{
    await Task.Delay(10, ct);
    if (id <= 0)
        return Error.Validation("User.InvalidId", "Invalid ID");
    return new List<Order> { new(1, id, 99.99m, DateTime.UtcNow) };
});

// Method group style
app.MapGet("/health", GetHealth);

app.Run();

// ═══════════════════════════════════════════════════════════════════════════════
// Handler methods - Can be inline lambdas or method groups
// ═══════════════════════════════════════════════════════════════════════════════

static ErrorOr<string> GetHealth() => "ok";

// ═══════════════════════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════════════════════

public record User(int Id, string Name, string Email);
public record CreateUserRequest(string Name, string Email);
public record Order(int Id, int UserId, decimal Total, DateTime CreatedAt);
