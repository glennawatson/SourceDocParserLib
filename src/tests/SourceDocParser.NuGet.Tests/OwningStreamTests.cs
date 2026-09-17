// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
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
    /// <summary>Expected fixture value used by ReadDelegatesToInner.</summary>
    private const long ReadDelegatesToInnerExpectedValue = 5L;

    /// <summary>Expected fixture value used by ReadDelegatesToInner.</summary>
    private const int ReadDelegatesToInnerExpectedValue2 = 5;

    /// <summary>Expected fixture value used by SpanReadDelegatesToInner.</summary>
    private const int SpanReadDelegatesToInnerExpectedValue = 3;

    /// <summary>Expected fixture value used by SeekAndPositionDelegate.</summary>
    private const long SeekAndPositionDelegateExpectedValue = 3L;

    /// <summary>Expected fixture value used by SeekAndPositionDelegate.</summary>
    private const int SeekAndPositionDelegateSeek = 2;

    /// <summary>Expected fixture value used by SeekAndPositionDelegate.</summary>
    private const long SeekAndPositionDelegateExpectedValue2 = 2L;

    /// <summary>Bytes expected after write delegation and stream truncation.</summary>
    private static readonly byte[] ExpectedWrittenBytes = [1, SeekAndPositionDelegateSeek, SpanReadDelegatesToInnerExpectedValue];

    /// <summary>Constructor rejects null arguments.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConstructorRejectsNullArgs()
    {
        await Assert.That(static () => new OwningStream(null!, new TrackingDisposable())).Throws<ArgumentNullException>();
        await Assert.That(static () => new OwningStream(new MemoryStream(), null!)).Throws<ArgumentNullException>();
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
        await Assert.That(sut.Length).IsEqualTo(ReadDelegatesToInnerExpectedValue);

        var buf = new byte[ReadDelegatesToInnerExpectedValue2];
        var read = await sut.ReadAsync(buf);
        await Assert.That(read).IsEqualTo(ReadDelegatesToInnerExpectedValue2);
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

        Span<byte> buf = stackalloc byte[SpanReadDelegatesToInnerExpectedValue];
        var read = sut.Read(buf);
        var bufValue = buf[0];
        await Assert.That(read).IsEqualTo(SpanReadDelegatesToInnerExpectedValue);
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

        var buf = new byte[SpanReadDelegatesToInnerExpectedValue];
        var read = await sut.ReadAsync(buf, CancellationToken.None).ConfigureAwait(false);
        await Assert.That(read).IsEqualTo(SpanReadDelegatesToInnerExpectedValue);

        inner.Position = 0;
        var memBuf = new byte[SpanReadDelegatesToInnerExpectedValue];
        var memRead = await sut.ReadAsync(memBuf.AsMemory()).ConfigureAwait(false);
        await Assert.That(memRead).IsEqualTo(SpanReadDelegatesToInnerExpectedValue);
    }

    /// <summary>Seek and Position delegate to the inner stream.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SeekAndPositionDelegate()
    {
        var inner = new MemoryStream(new byte[8]);
        await using var sut = new OwningStream(inner, new TrackingDisposable());

        sut.Position = SpanReadDelegatesToInnerExpectedValue;
        await Assert.That(sut.Position).IsEqualTo(SeekAndPositionDelegateExpectedValue);
        var pos = sut.Seek(SeekAndPositionDelegateSeek, SeekOrigin.Begin);
        await Assert.That(pos).IsEqualTo(SeekAndPositionDelegateExpectedValue2);
    }

    /// <summary>Write paths delegate to the inner stream.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WriteAndFlushDelegate()
    {
        var inner = new MemoryStream();
        await using var sut = new OwningStream(inner, new TrackingDisposable());

        byte[] data = [1, SeekAndPositionDelegateSeek, SpanReadDelegatesToInnerExpectedValue];
        await sut.WriteAsync(data);
        await sut.FlushAsync();
        sut.SetLength(SpanReadDelegatesToInnerExpectedValue);

        await Assert.That(inner.ToArray()).IsEquivalentTo(ExpectedWrittenBytes);
        await Assert.That(sut.CanWrite).IsTrue();
    }

    /// <summary>Synchronous Dispose disposes both inner stream and owner.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "Item under test is Dispose")]
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

    /// <summary>Test helper -- disposable that records when it was disposed.</summary>
    private sealed class TrackingDisposable : IDisposable
    {
        /// <summary>Gets a value indicating whether this instance is disposed.</summary>
        public bool Disposed { get; private set; }

        /// <inheritdoc/>
        public void Dispose() => Disposed = true;
    }

    /// <summary>Test helper -- memory-backed stream that records disposal.</summary>
    private sealed class TrackingStream : MemoryStream
    {
        /// <summary>Gets a value indicating whether this instance is disposed.</summary>
        public bool Disposed { get; private set; }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
