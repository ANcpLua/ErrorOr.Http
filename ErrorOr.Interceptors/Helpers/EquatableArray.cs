using System;
using System.Collections.Immutable;

namespace ANcpLua.Interceptors.ErrorOr.Generator.Helpers;

/// <summary>
/// Wraps ImmutableArray for proper equality comparison in incremental generators.
/// Uses element-wise comparison and precomputed hash.
/// </summary>
internal readonly record struct EquatableArray<T>(ImmutableArray<T> Items)
    where T : IEquatable<T>
{
    public static readonly EquatableArray<T> Empty = new(ImmutableArray<T>.Empty);

    public bool IsDefaultOrEmpty => Items.IsDefaultOrEmpty;

    public bool Equals(EquatableArray<T> other)
    {
        if (Items.IsDefault && other.Items.IsDefault) return true;
        if (Items.IsDefault || other.Items.IsDefault) return false;

        return Items.AsSpan().SequenceEqual(other.Items.AsSpan());
    }

    public override int GetHashCode()
    {
        if (Items.IsDefault) return 0;

        unchecked
        {
            var hash = 17;
            foreach (var item in Items)
                hash = hash * 31 + (item?.GetHashCode() ?? 0);
            return hash;
        }
    }
}
