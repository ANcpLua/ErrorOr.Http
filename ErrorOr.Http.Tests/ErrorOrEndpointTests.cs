using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ErrorOr.Http.Tests;

public class ErrorOrEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ErrorOrEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }


    [Fact]
    public async Task GetAll_ReturnsOkWithTodos()
    {
        var response = await _client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var todos = await response.Content.ReadFromJsonAsync<TodoDto[]>();
        todos.Should().NotBeNull();
        todos.Should().HaveCountGreaterThan(0);
    }


    [Fact]
    public async Task GetById_WhenValidId_ReturnsOk()
    {
        // First get all to find a valid ID
        var allResponse = await _client.GetAsync("/");
        var todos = await allResponse.Content.ReadFromJsonAsync<TodoDto[]>();
        var validId = todos![0].Id;

        var response = await _client.GetAsync($"/{validId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var todo = await response.Content.ReadFromJsonAsync<TodoDto>();
        todo.Should().NotBeNull();
        todo.Id.Should().Be(validId);
    }


    [Fact]
    public async Task GetById_WhenNotFoundId_Returns404()
    {
        var nonExistentId = Guid.NewGuid();
        var response = await _client.GetAsync($"/{nonExistentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }


    [Fact]
    public async Task Create_WhenValidRequest_ReturnsCreated()
    {
        var request = new { Id = Guid.Empty, Title = "New Todo" };

        var response = await _client.PostAsJsonAsync("/", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var todo = await response.Content.ReadFromJsonAsync<TodoDto>();
        todo.Should().NotBeNull();
        todo.Title.Should().Be("New Todo");
    }


    [Fact]
    public async Task Create_WhenMissingTitle_ReturnsValidationProblem()
    {
        var request = new { Id = Guid.Empty, Title = "" };

        var response = await _client.PostAsJsonAsync("/", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }


    [Fact]
    public async Task Delete_WhenValidId_ReturnsNoContent()
    {
        // First get all to find a valid ID
        var allResponse = await _client.GetAsync("/");
        var todos = await allResponse.Content.ReadFromJsonAsync<TodoDto[]>();
        var validId = todos![0].Id;

        var response = await _client.DeleteAsync($"/{validId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }


    [Fact]
    public async Task Delete_WhenNotFoundId_Returns404()
    {
        var nonExistentId = Guid.NewGuid();
        var response = await _client.DeleteAsync($"/{nonExistentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }


    private record TodoDto(Guid Id, string? Title, DateOnly? DueBy = null, bool IsComplete = false);
}
