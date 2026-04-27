// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Text;
using SourceDocParser.NuGet.Infrastructure;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins the disposal contract and read-delegation surface of
/// <see cref="OwningStream"/> using an in-memory inner stream and a
/// tracking owner so the contract can be exercised without HTTP.
/// </summary>
public class OwningStreamTests
{
    /// <summary>Constructor rejects null arguments.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConstructorRejectsNullArgs()
    {
        await Assert.That(() => new OwningStream(null!, new TrackingDisposable())).Throws<ArgumentNullException>();
        await Assert.That(() => new OwningStream(new MemoryStream(), null!)).Throws<ArgumentNullException>();
    }

    /// <summary>Reads delegate to the inner stream.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadDelegatesToInner()
    {
        var data = "hello"u8.ToArray();
        var inner = new MemoryStream(data);
        var owner = new TrackingDisposable();
        await using var sut = new OwningStream(inner, owner);

        await Assert.That(sut.CanRead).IsTrue();
        await Assert.That(sut.CanSeek).IsTrue();
        await Assert.That(sut.CanWrite).IsTrue();
        await Assert.That(sut.Length).IsEqualTo(5L);

        var buf = new byte[5];
        var read = await sut.ReadAsync(buf);
        await Assert.That(read).IsEqualTo(5);
        await Assert.That(Encoding.UTF8.GetString(buf)).IsEqualTo("hello");
    }

    /// <summary>Span-based read delegates correctly.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SpanReadDelegatesToInner()
    {
        var inner = new MemoryStream("abc"u8.ToArray());
        var owner = new TrackingDisposable();
        await using var sut = new OwningStream(inner, owner);

        Span<byte> buf = stackalloc byte[3];
        var read = sut.Read(buf);
        var bufValue = buf[0];
        await Assert.That(read).IsEqualTo(3);
        await Assert.That(bufValue).IsEqualTo((byte)'a');
    }

    /// <summary>Async reads delegate to the inner stream.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadAsyncDelegatesToInner()
    {
        var inner = new MemoryStream("xyz"u8.ToArray());
        var owner = new TrackingDisposable();
        await using var sut = new OwningStream(inner, owner);

        var buf = new byte[3];
        var read = await sut.ReadAsync(buf, CancellationToken.None).ConfigureAwait(false);
        await Assert.That(read).IsEqualTo(3);

        inner.Position = 0;
        var memBuf = new byte[3];
        var memRead = await sut.ReadAsync(memBuf.AsMemory()).ConfigureAwait(false);
        await Assert.That(memRead).IsEqualTo(3);
    }

    /// <summary>Seek and Position delegate to the inner stream.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SeekAndPositionDelegate()
    {
        var inner = new MemoryStream(new byte[8]);
        await using var sut = new OwningStream(inner, new TrackingDisposable());

        sut.Position = 3;
        await Assert.That(sut.Position).IsEqualTo(3L);
        var pos = sut.Seek(2, SeekOrigin.Begin);
        await Assert.That(pos).IsEqualTo(2L);
    }

    /// <summary>Write paths delegate to the inner stream.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WriteAndFlushDelegate()
    {
        var inner = new MemoryStream();
        await using var sut = new OwningStream(inner, new TrackingDisposable());

        byte[] data = [1, 2, 3];
        await sut.WriteAsync(data);
        await sut.FlushAsync();
        sut.SetLength(3);

        await Assert.That(inner.ToArray()).IsEquivalentTo(new byte[] { 1, 2, 3 });
        await Assert.That(sut.CanWrite).IsTrue();
    }

    /// <summary>Synchronous Dispose disposes both inner stream and owner.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "Item under test is Dispose")]
    [SuppressMessage("Major Code Smell", "S6966:Awaitable method should be used", Justification = "Item under test is Dispose")]
    public async Task DisposeDisposesInnerAndOwner()
    {
        var inner = new TrackingStream();
        var owner = new TrackingDisposable();
        var sut = new OwningStream(inner, owner);

        sut.Dispose();

        await Assert.That(inner.Disposed).IsTrue();
        await Assert.That(owner.Disposed).IsTrue();
    }

    /// <summary>Async Dispose disposes both inner stream and owner.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DisposeAsyncDisposesInnerAndOwner()
    {
        var inner = new TrackingStream();
        var owner = new TrackingDisposable();
        var sut = new OwningStream(inner, owner);

        await sut.DisposeAsync().ConfigureAwait(false);

        await Assert.That(inner.Disposed).IsTrue();
        await Assert.That(owner.Disposed).IsTrue();
    }

    /// <summary>Test helper — disposable that records when it was disposed.</summary>
    private sealed class TrackingDisposable : IDisposable
    {
        /// <summary>
        /// Gets a value indicating whether this instance is disposed.
        /// </summary>
        public bool Disposed { get; private set; }

        /// <inheritdoc/>
        public void Dispose() => Disposed = true;
    }

    /// <summary>Test helper — memory-backed stream that records disposal.</summary>
    private sealed class TrackingStream : MemoryStream
    {
        /// <summary>
        /// Gets a value indicating whether this instance is disposed.
        /// </summary>
        public bool Disposed { get; private set; }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
