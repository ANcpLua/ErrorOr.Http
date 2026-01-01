using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr.Http.Sample;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using ErrorOr.Http;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolver = TodoJsonContext.Default;
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment()) app.MapOpenApi();

// Sample data
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
internal partial class TodoJsonContext : JsonSerializerContext { }

namespace ErrorOr.Http.Sample
{
    public record Todo(int Id, string? Title, DateOnly? DueBy = null, bool IsComplete = false);

    public static class TodoEndpoints
    {
        public static Todo[] SampleTodos { get; set; } = [];

        [ErrorOrEndpoint("GET", "/")]
        public static ErrorOr<Todo[]> GetAll() => SampleTodos;

        [ErrorOrEndpoint("GET", "/{id}")]
        public static ErrorOr<Todo> GetById(int id)
            => SampleTodos.FirstOrDefault(t => t.Id == id) is { } todo
                ? todo
                : Error.NotFound("Todo.NotFound", $"Todo with id {id} not found");

        [ErrorOrEndpoint("POST", "/")]
        public static ErrorOr<Todo> Create([FromBody] Todo todo)
        {
            if (string.IsNullOrWhiteSpace(todo.Title))
                return Error.Validation("Todo.InvalidTitle", "Title is required");

            return todo with { Id = SampleTodos.Length + 1 };
        }

        [ErrorOrEndpoint("DELETE", "/{id}")]
        public static ErrorOr<Deleted> Delete(int id)
        {
            if (SampleTodos.All(t => t.Id != id))
                return Error.NotFound("Todo.NotFound", $"Todo with id {id} not found");

            return Result.Deleted;
        }
    }
}