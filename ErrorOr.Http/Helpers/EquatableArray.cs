using System.Collections.Immutable;

namespace ErrorOr.Http.Helpers;

/// <summary>
///     Wraps ImmutableArray for proper equality comparison in incremental generators.
///     Uses element-wise comparison and HashCode for efficient hashing.
/// </summary>
internal readonly record struct EquatableArray<T>(ImmutableArray<T> Items)
    where T : IEquatable<T>
{
    public static readonly EquatableArray<T> Empty = new(ImmutableArray<T>.Empty);

    public bool IsDefaultOrEmpty
    {
        get => Items.IsDefaultOrEmpty;
    }

    // Suppress EPS06: ReadOnlySpan is designed to be passed by value (16 bytes: pointer + length)
#pragma warning disable EPS06
    public bool Equals(EquatableArray<T> other)
    {
        if (Items.IsDefault && other.Items.IsDefault) return true;
        if (Items.IsDefault || other.Items.IsDefault) return false;

        return Items.AsSpan().SequenceEqual(other.Items.AsSpan());
    }
#pragma warning restore EPS06

    public override int GetHashCode()
    {
        if (Items.IsDefault) return 0;

        var hash = new HashCode();
        foreach (var item in Items)
            hash.Add(item);
        return hash.ToHashCode();
    }
}
