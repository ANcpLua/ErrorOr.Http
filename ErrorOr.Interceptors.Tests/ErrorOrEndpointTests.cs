using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ErrorOr.Interceptors.Tests;

public class ErrorOrEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ErrorOrEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // GET /users/{id} - Success Paths
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetUser_WhenValidId_ReturnsOk()
    {
        var response = await _client.GetAsync("/users/1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        user.Should().NotBeNull();
        user.Id.Should().Be(1);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // GET /users/{id} - Error Paths
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetUser_WhenNotFoundId_Returns404()
    {
        var response = await _client.GetAsync("/users/404");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetUser_WhenInvalidId_ReturnsValidationProblem()
    {
        var response = await _client.GetAsync("/users/-1");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // POST /users - Success Paths
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateUser_WhenValidRequest_ReturnsOk()
    {
        // Note: Returns 200 OK, not 201 Created. The interceptor adds OpenAPI metadata
        // (.Produces<>(201)) but runtime uses ErrorOr's implicit conversion to IResult
        // which returns Ok. A runtime filter would be needed for 201 Created.
        var request = new { Name = "Test User", Email = "test@example.com" };

        var response = await _client.PostAsJsonAsync("/users", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        user.Should().NotBeNull();
        user.Name.Should().Be("Test User");
        user.Email.Should().Be("test@example.com");
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // POST /users - Error Paths
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateUser_WhenMissingName_ReturnsValidationProblem()
    {
        var request = new { Name = "", Email = "test@example.com" };

        var response = await _client.PostAsJsonAsync("/users", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateUser_WhenEmailConflict_ReturnsConflict()
    {
        var request = new { Name = "Test", Email = "exists@example.com" };

        var response = await _client.PostAsJsonAsync("/users", request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // DELETE /users/{id} - Success Paths
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteUser_WhenValidId_ReturnsNoContent()
    {
        var response = await _client.DeleteAsync("/users/1");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // DELETE /users/{id} - Error Paths
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteUser_WhenNotFoundId_Returns404()
    {
        var response = await _client.DeleteAsync("/users/404");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // GET /users/{id}/orders - Async Endpoints
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetUserOrders_WhenValidId_ReturnsOrders()
    {
        var response = await _client.GetAsync("/users/1/orders");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var orders = await response.Content.ReadFromJsonAsync<List<OrderDto>>();
        orders.Should().NotBeNull();
        orders.Should().HaveCountGreaterThan(0);
        orders[0].Id.Should().BePositive();
        orders[0].UserId.Should().Be(1);
        orders[0].Total.Should().BePositive();
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // DTOs
    // ═══════════════════════════════════════════════════════════════════════════════

    private record UserDto(int Id, string Name, string Email);

    private record OrderDto(int Id, int UserId, decimal Total);
}