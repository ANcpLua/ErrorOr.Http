//HintName: ErrorOrJsonContext.suggested.cs
// SUGGESTED JSON CONTEXT FOR NATIVE AOT
#if ERROROR_JSON
[System.Text.Json.Serialization.JsonSourceGenerationOptions(DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
[System.Text.Json.Serialization.JsonSerializable(typeof(global::Microsoft.AspNetCore.Mvc.ProblemDetails))]
[System.Text.Json.Serialization.JsonSerializable(typeof(global::Microsoft.AspNetCore.Http.HttpValidationProblemDetails))]
internal partial class ErrorOrJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
#endif
