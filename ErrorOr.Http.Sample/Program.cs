using System.Text.Json.Serialization;
using ErrorOr.Http.Generated;
using ErrorOr.Http.Sample;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolver = TodoJsonContext.Default;
});

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.MapErrorOrEndpoints();

TodoEndpoints.SampleTodos =
[
    new Todo(1, "Walk the dog"),
    new Todo(2, "Do the dishes", DateOnly.FromDateTime(DateTime.Now)),
    new Todo(3, "Do the laundry", DateOnly.FromDateTime(DateTime.Now.AddDays(1))),
    new Todo(4, "Clean the bathroom"),
    new Todo(5, "Clean the car", DateOnly.FromDateTime(DateTime.Now.AddDays(2)))
];

app.Run();

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Todo[]))]
[JsonSerializable(typeof(Todo))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
internal partial class TodoJsonContext : JsonSerializerContext
{
}

namespace ErrorOr.Http.Sample
{
    public record Todo(int Id, string? Title, DateOnly? DueBy = null, bool IsComplete = false);

    public static class TodoEndpoints
    {
        public static Todo[] SampleTodos { get; set; } = [];

        [Get]
        public static ErrorOr<Todo[]> GetAll()
        {
            return SampleTodos;
        }

        [Get("/{id}")]
        public static ErrorOr<Todo> GetById(int id)
        {
            return SampleTodos.FirstOrDefault(t => t.Id == id) is { } todo
                ? todo
                : Error.NotFound("Todo.NotFound", $"Todo with id {id} not found");
        }

        [Post]
        public static ErrorOr<Todo> Create([FromBody] Todo todo)
        {
            if (string.IsNullOrWhiteSpace(todo.Title))
                return Error.Validation("Todo.InvalidTitle", "Title is required");

            return todo with { Id = SampleTodos.Length + 1 };
        }

        [Delete("/{id}")]
        public static ErrorOr<Deleted> Delete(int id)
        {
            if (SampleTodos.All(t => t.Id != id))
                return Error.NotFound("Todo.NotFound", $"Todo with id {id} not found");

            return Result.Deleted;
        }
    }
}
