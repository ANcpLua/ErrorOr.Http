using System.Text.Json.Serialization;
using ErrorOr;
using ErrorOr.Interceptors;
using Microsoft.AspNetCore.Http.HttpResults;
using WebApplication1;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Sample data
TodoEndpoints.SampleTodos =
[
    new Todo(1, "Walk the dog"),
    new Todo(2, "Do the dishes", DateOnly.FromDateTime(DateTime.Now)),
    new Todo(3, "Do the laundry", DateOnly.FromDateTime(DateTime.Now.AddDays(1))),
    new Todo(4, "Clean the bathroom"),
    new Todo(5, "Clean the car", DateOnly.FromDateTime(DateTime.Now.AddDays(2)))
];

// Map ErrorOr endpoints using generated extension (AOT-safe, no reflection)
app.MapTodoEndpoints();

app.Run();

namespace WebApplication1
{
    public record Todo(int Id, string? Title, DateOnly? DueBy = null, bool IsComplete = false);

    /// <summary>
    /// ErrorOr endpoints - all methods are static, returning ErrorOr&lt;T&gt;.
    /// The generator creates typed lambdas with full AOT support.
    /// </summary>
    [ErrorOrEndpoints(Prefix = "/todos", Tag = "Todos")]
    public static class TodoEndpoints
    {
        public static Todo[] SampleTodos { get; set; } = [];

        [ErrorOrGet("", Name = "GetAllTodos", Summary = "Get all todos")]
        public static ErrorOr<Todo[]> GetAll()
        {
            return SampleTodos;
        }

        [ErrorOrGet("/{id}", Name = "GetTodoById", Summary = "Get a todo by ID")]
        public static ErrorOr<Todo> GetById(int id)
        {
            return SampleTodos.FirstOrDefault(t => t.Id == id) is { } todo
                ? todo
                : Error.NotFound("Todo.NotFound", $"Todo with id {id} not found");
        }

        [ErrorOrPost("", Name = "CreateTodo", Summary = "Create a new todo")]
        public static ErrorOr<Todo> Create([Microsoft.AspNetCore.Mvc.FromBody] Todo todo)
        {
            if (string.IsNullOrWhiteSpace(todo.Title))
                return Error.Validation("Todo.InvalidTitle", "Title is required");

            // In real app, would save to DB
            return todo with { Id = SampleTodos.Length + 1 };
        }

        [ErrorOrDelete("/{id}", Name = "DeleteTodo", Summary = "Delete a todo")]
        public static ErrorOr<Deleted> Delete(int id)
        {
            if (SampleTodos.All(t => t.Id != id))
                return Error.NotFound("Todo.NotFound", $"Todo with id {id} not found");

            // In real app, would delete from DB
            return Result.Deleted;
        }
    }

    [JsonSerializable(typeof(Todo))]
    [JsonSerializable(typeof(Todo[]))]
    [JsonSerializable(typeof(Microsoft.AspNetCore.Http.HttpValidationProblemDetails))]
    [JsonSerializable(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {
    }
}
