using ErrorOr;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddSingleton<UserService>();

var app = builder.Build();
app.MapOpenApi();

// Clean endpoints - just call services
app.MapGet("/users/{id}", (int id, UserService service) => service.GetById(id));

app.MapPost("/users", ([FromBody] CreateUserRequest request, UserService service) => service.Create(request));

app.MapDelete("/users/{id}", (int id, UserService service) => service.Delete(id));

app.MapGet("/users/{id}/orders", (int id, UserService service, CancellationToken ct) => service.GetOrdersAsync(id, ct));

app.Run();

// Service - contains business logic, returns ErrorOr<T>, knows nothing about HTTP
public class UserService
{
    private readonly Dictionary<int, User> _users = new()
    {
        [1] = new User(1, "Alice", "alice@example.com"),
        [2] = new User(2, "Bob", "bob@example.com")
    };

    public ErrorOr<User> GetById(int id)
    {
        if (id <= 0)
            return Error.Validation("User.InvalidId", "ID must be positive");

        if (!_users.TryGetValue(id, out var user))
            return Error.NotFound("User.NotFound", $"User {id} not found");

        return user;
    }

    public ErrorOr<User> Create(CreateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Error.Validation("User.NameRequired", "Name is required");

        if (string.IsNullOrWhiteSpace(request.Email))
            return Error.Validation("User.EmailRequired", "Email is required");

        if (_users.Values.Any(u => u.Email == request.Email))
            return Error.Conflict("User.EmailExists", "Email already registered");

        var user = new User(Random.Shared.Next(100, 999), request.Name, request.Email);
        _users[user.Id] = user;
        return user;
    }

    public ErrorOr<Deleted> Delete(int id)
    {
        if (id <= 0)
            return Error.Validation("User.InvalidId", "ID must be positive");

        if (!_users.Remove(id))
            return Error.NotFound("User.NotFound", $"User {id} not found");

        return Result.Deleted;
    }

    public async Task<ErrorOr<List<Order>>> GetOrdersAsync(int id, CancellationToken ct)
    {
        await Task.Delay(10, ct); // Simulate DB call

        if (id <= 0)
            return Error.Validation("User.InvalidId", "ID must be positive");

        if (!_users.ContainsKey(id))
            return Error.NotFound("User.NotFound", $"User {id} not found");

        return new List<Order>
        {
            new(1, id, 29.99m, DateTime.UtcNow.AddDays(-7)),
            new(2, id, 149.99m, DateTime.UtcNow.AddDays(-1))
        };
    }
}

// DTOs
public record User(int Id, string Name, string Email);
public record CreateUserRequest(string Name, string Email);
public record Order(int Id, int UserId, decimal Total, DateTime CreatedAt);
