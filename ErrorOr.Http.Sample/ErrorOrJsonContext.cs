using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr.Http.Sample;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Todo[]))]
[JsonSerializable(typeof(Todo))]
internal partial class ErrorOrJsonContext : JsonSerializerContext { }