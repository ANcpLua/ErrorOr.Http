//HintName: ErrorOrEndpointAttribute.g.cs
using System;

namespace ErrorOr.Http
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class ErrorOrEndpointAttribute : Attribute
    {
        public ErrorOrEndpointAttribute(string httpMethod, string pattern)
        {
            HttpMethod = httpMethod;
            Pattern = pattern;
        }

        public string HttpMethod { get; }
        public string Pattern { get; }
    }
}
