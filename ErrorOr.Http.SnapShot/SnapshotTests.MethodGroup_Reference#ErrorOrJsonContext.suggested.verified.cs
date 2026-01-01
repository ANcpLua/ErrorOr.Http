//HintName: ErrorOrJsonContext.suggested.cs
// =============================================================================
// SUGGESTED JSON CONTEXT FOR NATIVE AOT
// =============================================================================
// To enable NativeAOT JSON serialization:
// 1. Create a new file ErrorOrJsonContext.cs in your project
// 2. Copy the code below (between #if ERROROR_JSON and #endif)
// 3. Add: builder.Services.AddErrorOrEndpointJson<ErrorOrJsonContext>();
// =============================================================================

#if ERROROR_JSON // Remove this line when copying to your project

#nullable enable
using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(global::Microsoft.AspNetCore.Mvc.ProblemDetails))]
[JsonSerializable(typeof(global::Microsoft.AspNetCore.Http.HttpValidationProblemDetails))]
internal partial class ErrorOrJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}

#endif // Remove this line when copying to your project
