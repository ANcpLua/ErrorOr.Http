namespace ErrorOr.Http.Generators;

/// <summary>
///     Centralized well-known type names for ASP.NET Core and ErrorOr.Http.
///     Following the Roslyn pattern for WellKnownTypes/WellKnownTags.
/// </summary>
internal static class WellKnownTypes
{

    public const string ErrorOrEndpointAttribute = "ErrorOr.Http.ErrorOrEndpointAttribute";
    public const string GetAttribute = "ErrorOr.Http.GetAttribute";
    public const string PostAttribute = "ErrorOr.Http.PostAttribute";
    public const string PutAttribute = "ErrorOr.Http.PutAttribute";
    public const string DeleteAttribute = "ErrorOr.Http.DeleteAttribute";
    public const string PatchAttribute = "ErrorOr.Http.PatchAttribute";


    public const string ErrorOrT = "ErrorOr.ErrorOr<TValue>";


    public const string FromBodyAttribute = "Microsoft.AspNetCore.Mvc.FromBodyAttribute";
    public const string FromFormAttribute = "Microsoft.AspNetCore.Mvc.FromFormAttribute";
    public const string FromHeaderAttribute = "Microsoft.AspNetCore.Mvc.FromHeaderAttribute";
    public const string FromQueryAttribute = "Microsoft.AspNetCore.Mvc.FromQueryAttribute";
    public const string FromRouteAttribute = "Microsoft.AspNetCore.Mvc.FromRouteAttribute";
    public const string FromServicesAttribute = "Microsoft.AspNetCore.Mvc.FromServicesAttribute";
    public const string ProblemDetails = "Microsoft.AspNetCore.Mvc.ProblemDetails";


    public const string AsParametersAttribute = "Microsoft.AspNetCore.Http.AsParametersAttribute";
    public const string HttpContext = "Microsoft.AspNetCore.Http.HttpContext";
    public const string HttpValidationProblemDetails = "Microsoft.AspNetCore.Http.HttpValidationProblemDetails";
    public const string IFormCollection = "Microsoft.AspNetCore.Http.IFormCollection";
    public const string IFormFile = "Microsoft.AspNetCore.Http.IFormFile";
    public const string IFormFileCollection = "Microsoft.AspNetCore.Http.IFormFileCollection";

    /// <summary>
    ///     Interface for custom parameter binding from HttpContext.
    ///     Signature: IBindableFromHttpContext&lt;TSelf&gt; where TSelf : class
    /// </summary>
    public const string IBindableFromHttpContext = "Microsoft.AspNetCore.Http.IBindableFromHttpContext`1";

    public const string ParameterInfo = "System.Reflection.ParameterInfo";


    public const string FromKeyedServicesAttribute =
        "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute";


    public const string CancellationToken = "System.Threading.CancellationToken";
    public const string ObsoleteAttribute = "System.ObsoleteAttribute";
    public const string JsonSerializableAttribute = "System.Text.Json.Serialization.JsonSerializableAttribute";
    public const string JsonSerializerContext = "System.Text.Json.Serialization.JsonSerializerContext";

    public const string TaskT = "System.Threading.Tasks.Task<TResult>";
    public const string ValueTaskT = "System.Threading.Tasks.ValueTask<TResult>";

    public const string Stream = "System.IO.Stream";
    public const string PipeReader = "System.IO.Pipelines.PipeReader";

    public const string String = "System.String";
    public const string Guid = "System.Guid";
    public const string DateTime = "System.DateTime";
    public const string DateTimeOffset = "System.DateTimeOffset";
    public const string DateOnly = "System.DateOnly";
    public const string TimeOnly = "System.TimeOnly";
    public const string TimeSpan = "System.TimeSpan";


    /// <summary>
    ///     SseItem&lt;T&gt; represents a single Server-Sent Event with typed data payload.
    ///     Used with TypedResults.ServerSentEvents() for streaming responses.
    /// </summary>
    public const string SseItemT = "System.Net.ServerSentEvents.SseItem<T>";

    /// <summary>
    ///     IAsyncEnumerable&lt;T&gt; for streaming sequences of data.
    ///     Used as the success type for SSE endpoints.
    /// </summary>
    public const string IAsyncEnumerableT = "System.Collections.Generic.IAsyncEnumerable<T>";


    public static class Fqn
    {
        public const string CancellationToken = "global::System.Threading.CancellationToken";
        public const string HttpContext = "global::Microsoft.AspNetCore.Http.HttpContext";
        public const string IFormCollection = "global::Microsoft.AspNetCore.Http.IFormCollection";
        public const string IFormFile = "global::Microsoft.AspNetCore.Http.IFormFile";
        public const string IFormFileCollection = "global::Microsoft.AspNetCore.Http.IFormFileCollection";
        public const string ProblemDetails = "global::Microsoft.AspNetCore.Mvc.ProblemDetails";

        public const string HttpValidationProblemDetails =
            "global::Microsoft.AspNetCore.Http.HttpValidationProblemDetails";

        public const string String = "global::System.String";
        public const string Guid = "global::System.Guid";
        public const string DateTime = "global::System.DateTime";
        public const string DateTimeOffset = "global::System.DateTimeOffset";
        public const string DateOnly = "global::System.DateOnly";
        public const string TimeOnly = "global::System.TimeOnly";
        public const string TimeSpan = "global::System.TimeSpan";

        public const string Stream = "global::System.IO.Stream";
        public const string PipeReader = "global::System.IO.Pipelines.PipeReader";
    }
}
