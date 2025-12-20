using System.Collections.Generic;

namespace ANcpLua.Interceptors.ErrorOr.Generator.Helpers;

// ═══════════════════════════════════════════════════════════════════════════════
// Pipeline Data Types - Single Source of Truth
// ═══════════════════════════════════════════════════════════════════════════════

internal readonly record struct MapCallSite(
    string HttpMethod,
    string SuccessTypeFqn,
    bool IsAsync,
    EquatableArray<int> InferredErrorTypes,
    string EncodedLocation);

// Note: HandlerParameter removed - not needed with Delegate passthrough approach

internal readonly record struct InterceptorSignature(
    string HttpMethod,
    string SuccessTypeFqn,
    bool IsAsync,
    EquatableArray<int> InferredErrorTypes);

internal readonly record struct InterceptorGroup(
    InterceptorSignature Signature,
    EquatableArray<string> Locations);

// Note: Routes removed - only Locations needed for interceptor attributes

// ═══════════════════════════════════════════════════════════════════════════════
// Signature Comparer for Grouping
// ═══════════════════════════════════════════════════════════════════════════════

internal sealed class InterceptorSignatureComparer : IEqualityComparer<InterceptorSignature>
{
    public static InterceptorSignatureComparer Instance { get; } = new();

    public bool Equals(InterceptorSignature x, InterceptorSignature y) =>
        x.HttpMethod == y.HttpMethod &&
        x.SuccessTypeFqn == y.SuccessTypeFqn &&
        x.IsAsync == y.IsAsync &&
        x.InferredErrorTypes.Equals(y.InferredErrorTypes);

    public int GetHashCode(InterceptorSignature obj)
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + obj.HttpMethod.GetHashCode();
            hash = hash * 31 + obj.SuccessTypeFqn.GetHashCode();
            hash = hash * 31 + obj.IsAsync.GetHashCode();
            hash = hash * 31 + obj.InferredErrorTypes.GetHashCode();
            return hash;
        }
    }
}