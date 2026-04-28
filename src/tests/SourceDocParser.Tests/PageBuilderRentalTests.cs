// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;

namespace SourceDocParser.Tests;

/// <summary>
/// Pins <see cref="PageBuilderRental"/> -- the disposable rental wrapper
/// over a pooled <see cref="StringBuilder"/>. Covers the implicit
/// conversion, equality / inequality operators, <c>Equals</c> overloads,
/// <c>GetHashCode</c>, and <c>Dispose</c> semantics (which routes the
/// builder back through <see cref="PageBuilderPool"/>).
/// </summary>
public class PageBuilderRentalTests
{
    /// <summary>The builder property exposes exactly the instance handed to the constructor.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuilderPropertyReturnsConstructorArgument()
    {
        var sb = new StringBuilder();
        var rental = new PageBuilderRental(sb);
        await Assert.That(rental.Builder).IsSameReferenceAs(sb);
    }

    /// <summary>The implicit conversion to <see cref="StringBuilder"/> unwraps the builder.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ImplicitConversionUnwrapsBuilder()
    {
        var sb = new StringBuilder("hello");
        StringBuilder unwrapped = new PageBuilderRental(sb);
        await Assert.That(unwrapped).IsSameReferenceAs(sb);
    }

    /// <summary>Two rentals wrapping the same builder are equal under all equality surfaces.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EqualityHoldsWhenWrappingSameBuilder()
    {
        var sb = new StringBuilder();
        var left = new PageBuilderRental(sb);
        var right = new PageBuilderRental(sb);

        await Assert.That(left.Equals(right)).IsTrue();
        await Assert.That(left.Equals((object)right)).IsTrue();
        await Assert.That(left == right).IsTrue();
        await Assert.That(left != right).IsFalse();
        await Assert.That(left.GetHashCode()).IsEqualTo(right.GetHashCode());
    }

    /// <summary>Rentals over distinct builders are unequal.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InequalityHoldsWhenWrappingDifferentBuilders()
    {
        var left = new PageBuilderRental(new StringBuilder());
        var right = new PageBuilderRental(new StringBuilder());

        await Assert.That(left.Equals(right)).IsFalse();
        await Assert.That(left == right).IsFalse();
        await Assert.That(left != right).IsTrue();
    }

    /// <summary><c>Equals(object)</c> returns false for a non-rental argument and for null.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EqualsObjectRejectsForeignTypesAndNull()
    {
        var rental = new PageBuilderRental(new StringBuilder());

        await Assert.That(rental.Equals((object?)null)).IsFalse();
        await Assert.That(rental.Equals("not a rental")).IsFalse();
        await Assert.That(rental.Equals(new object())).IsFalse();
    }

    /// <summary><c>GetHashCode</c> matches the wrapped builder's hash.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetHashCodeMatchesUnderlyingBuilderHash()
    {
        var sb = new StringBuilder("payload");
        var rental = new PageBuilderRental(sb);
        await Assert.That(rental.GetHashCode()).IsEqualTo(sb.GetHashCode());
    }

    /// <summary><c>GetHashCode</c> falls back to zero when the wrapped builder is null (default struct).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetHashCodeReturnsZeroForDefaultStruct()
    {
        var rental = default(PageBuilderRental);
        await Assert.That(rental.GetHashCode()).IsEqualTo(0);
    }

    /// <summary>
    /// <c>Dispose</c> hands the builder back to the pool, so a subsequent
    /// <see cref="PageBuilderPool.Rent(int)"/> call on the same thread
    /// reuses the same instance (cleared).
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DisposeReturnsBuilderToPool()
    {
        // Drain whatever is parked first so the assertion is deterministic.
        using (PageBuilderPool.Rent(0))
        {
            // discard
        }

        StringBuilder captured;
        using (var rental = PageBuilderPool.Rent(64))
        {
            captured = rental.Builder;
            captured.Append("dirty");
        }

        using var next = PageBuilderPool.Rent(0);
        await Assert.That(next.Builder).IsSameReferenceAs(captured);
        await Assert.That(next.Builder.Length).IsEqualTo(0);
    }

    /// <summary>
    /// Builders that exceed the pool's capacity ceiling are dropped on
    /// dispose rather than parked, so the next rent yields a different
    /// instance.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DisposeDropsOversizedBuilder()
    {
        // Drain the per-thread cache.
        using (PageBuilderPool.Rent(0))
        {
            // discard
        }

        const int OverCap = (64 * 1024) + 1;

        StringBuilder oversized;
        using (var rental = PageBuilderPool.Rent(OverCap))
        {
            oversized = rental.Builder;
        }

        using var next = PageBuilderPool.Rent(0);
        await Assert.That(next.Builder).IsNotSameReferenceAs(oversized);
    }
}
