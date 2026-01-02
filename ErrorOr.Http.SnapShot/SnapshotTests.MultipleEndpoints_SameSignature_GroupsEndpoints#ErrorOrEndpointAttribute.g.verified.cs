//HintName: ErrorOrEndpointAttribute.g.cs
using System;

namespace ErrorOr.Http
{
    /// <summary>
    /// Base class for HTTP endpoint attributes.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public abstract class ErrorOrEndpointAttribute : Attribute
    {
        protected ErrorOrEndpointAttribute(string httpMethod, string pattern = "/")
        {
            HttpMethod = httpMethod;
            Pattern = pattern;
        }

        public string HttpMethod { get; }
        public string Pattern { get; }
    }

    /// <summary>HTTP GET endpoint.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class GetAttribute : ErrorOrEndpointAttribute
    {
        public GetAttribute(string pattern = "/") : base("GET", pattern) { }
    }

    /// <summary>HTTP POST endpoint.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class PostAttribute : ErrorOrEndpointAttribute
    {
        public PostAttribute(string pattern = "/") : base("POST", pattern) { }
    }

    /// <summary>HTTP PUT endpoint.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class PutAttribute : ErrorOrEndpointAttribute
    {
        public PutAttribute(string pattern = "/") : base("PUT", pattern) { }
    }

    /// <summary>HTTP DELETE endpoint.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class DeleteAttribute : ErrorOrEndpointAttribute
    {
        public DeleteAttribute(string pattern = "/") : base("DELETE", pattern) { }
    }

    /// <summary>HTTP PATCH endpoint.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class PatchAttribute : ErrorOrEndpointAttribute
    {
        public PatchAttribute(string pattern = "/") : base("PATCH", pattern) { }
    }
}