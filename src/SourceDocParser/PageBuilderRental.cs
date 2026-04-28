// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;

namespace SourceDocParser;

/// <summary>
/// Disposable rental over a pooled <see cref="StringBuilder"/>. Use
/// inside a <c>using</c> declaration so the builder returns to the
/// pool on scope exit.
/// </summary>
internal readonly struct PageBuilderRental : IEquatable<PageBuilderRental>, IDisposable
{
    /// <summary>Initializes a new instance of the <see cref="PageBuilderRental"/> struct wrapping <paramref name="builder"/>.</summary>
    /// <param name="builder">Builder pulled from the pool.</param>
    internal PageBuilderRental(StringBuilder builder)
    {
        Builder = builder;
    }

    /// <summary>Gets the underlying <see cref="StringBuilder"/>; valid until <see cref="Dispose"/>.</summary>
    public StringBuilder Builder { get; }

    /// <summary>Lets callers pass the rental anywhere a <see cref="StringBuilder"/> is expected.</summary>
    /// <param name="rental">The rental to unwrap.</param>
    public static implicit operator StringBuilder(PageBuilderRental rental) => rental.Builder;

    /// <summary>Equality operator over the wrapped builder reference.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when both rentals wrap the same builder.</returns>
    public static bool operator ==(PageBuilderRental left, PageBuilderRental right) => left.Equals(right);

    /// <summary>Inequality operator over the wrapped builder reference.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when the rentals wrap different builders.</returns>
    public static bool operator !=(PageBuilderRental left, PageBuilderRental right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(PageBuilderRental other) => ReferenceEquals(Builder, other.Builder);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PageBuilderRental other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Builder?.GetHashCode() ?? 0;

    /// <inheritdoc />
    public void Dispose() => PageBuilderPool.Return(Builder);
}
