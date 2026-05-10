// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;

namespace SourceDocParser.Tests;

/// <summary>
/// Pins <see cref="CallbackPageSink"/> on the contract that backs the streaming
/// emit path: the constructor's null guard, both <c>WritePage</c> overloads
/// faithfully delivering <c>(relativePath, utf8Bytes)</c> to the supplied callback
/// (synchronously inline with the call), the argument-validation guards on the
/// <c>relativePath</c> / <c>builder</c> parameters, the multi-chunk
/// <see cref="StringBuilder"/> encode path, and the
/// <see cref="System.Threading.CancellationToken"/> short-circuit on the async
/// overload.
/// </summary>
public class CallbackPageSinkTests
{
    /// <summary>Forces the StringBuilder to grow across multiple internal chunks so the encode loop iterates more than once.</summary>
    private const int MultiChunkAppendCount = 4096;

    /// <summary>Repeating payload appended to drive the multi-chunk encode case; mixes ASCII and multi-byte UTF-8 to exercise the encoder.</summary>
    private const string MultiChunkSegment = "abcdefghijklmnopqrstuvwxyz0123456789-éñ漢";

    /// <summary>The constructor rejects a <see langword="null"/> callback.</summary>
    /// <returns>Async test.</returns>
    [Test]
    public async Task ConstructorThrowsWhenCallbackIsNull() =>
        await Assert.That(static () => new CallbackPageSink(null!)).Throws<ArgumentNullException>();

    /// <summary><c>WritePage</c> delivers the path and UTF-8-encoded bytes to the callback once, in-line.</summary>
    /// <returns>Async test.</returns>
    [Test]
    public async Task WritePageInvokesCallbackWithUtf8Bytes()
    {
        string? capturedPath = null;
        byte[]? capturedBytes = null;
        var sink = new CallbackPageSink((path, bytes) =>
        {
            capturedPath = path;
            capturedBytes = bytes;
        });

        var builder = new StringBuilder("Hello, world! éñ漢");
        sink.WritePage("api/Foo.md", builder);

        await Assert.That(capturedPath).IsEqualTo("api/Foo.md");
        await Assert.That(capturedBytes).IsNotNull();
        await Assert.That(Encoding.UTF8.GetString(capturedBytes!)).IsEqualTo("Hello, world! éñ漢");
    }

    /// <summary>An empty builder produces a zero-length payload.</summary>
    /// <returns>Async test.</returns>
    [Test]
    public async Task WritePageEncodesEmptyBuilderAsZeroBytes()
    {
        byte[]? capturedBytes = null;
        var sink = new CallbackPageSink((_, bytes) => capturedBytes = bytes);

        sink.WritePage("p.md", new StringBuilder());

        await Assert.That(capturedBytes).IsNotNull();
        await Assert.That(capturedBytes!.Length).IsEqualTo(0);
    }

    /// <summary>A multi-chunk <see cref="StringBuilder"/> encodes byte-identically to the equivalent string round-tripped through UTF-8.</summary>
    /// <returns>Async test.</returns>
    [Test]
    public async Task WritePageEncodesMultiChunkBuilder()
    {
        byte[]? capturedBytes = null;
        var sink = new CallbackPageSink((_, bytes) => capturedBytes = bytes);

        var builder = new StringBuilder();
        for (var i = 0; i < MultiChunkAppendCount; i++)
        {
            builder.Append(MultiChunkSegment);
        }

        sink.WritePage("big.md", builder);

        var expected = Encoding.UTF8.GetByteCount(builder.ToString());
        await Assert.That(capturedBytes).IsNotNull();
        await Assert.That(capturedBytes!.Length).IsEqualTo(expected);
        await Assert.That(Encoding.UTF8.GetString(capturedBytes)).IsEqualTo(builder.ToString());
    }

    /// <summary><c>WritePage</c> rejects null / blank relative paths.</summary>
    /// <param name="relativePath">Invalid candidate.</param>
    /// <returns>Async test.</returns>
    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task WritePageThrowsWhenRelativePathIsBlank(string relativePath)
    {
        var sink = new CallbackPageSink(static (_, _) => { });
        await Assert.That(() => sink.WritePage(relativePath, new StringBuilder("x"))).Throws<ArgumentException>();
    }

    /// <summary><c>WritePage</c> rejects a <see langword="null"/> builder.</summary>
    /// <returns>Async test.</returns>
    [Test]
    public async Task WritePageThrowsWhenBuilderIsNull()
    {
        var sink = new CallbackPageSink(static (_, _) => { });
        await Assert.That(() => sink.WritePage("p.md", null!)).Throws<ArgumentNullException>();
    }

    /// <summary><c>WritePageAsync</c> delegates to the sync path and returns a completed task.</summary>
    /// <returns>Async test.</returns>
    [Test]
    public async Task WritePageAsyncDelegatesAndCompletesSynchronously()
    {
        var invocations = 0;
        var sink = new CallbackPageSink((_, _) => invocations++);

        var task = sink.WritePageAsync("p.md", new StringBuilder("x"), CancellationToken.None);

        await Assert.That(task.IsCompletedSuccessfully).IsTrue();
        await Assert.That(invocations).IsEqualTo(1);
    }

    /// <summary><c>WritePageAsync</c> short-circuits on a pre-cancelled token before invoking the callback.</summary>
    /// <returns>Async test.</returns>
    [Test]
    public async Task WritePageAsyncObservesCancellationBeforeInvokingCallback()
    {
        var invocations = 0;
        var sink = new CallbackPageSink((_, _) => invocations++);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.That(() => sink.WritePageAsync("p.md", new StringBuilder("x"), cts.Token)).Throws<OperationCanceledException>();
        await Assert.That(invocations).IsEqualTo(0);
    }

    /// <summary><c>WritePageAsync</c> rejects null / blank relative paths.</summary>
    /// <param name="relativePath">Invalid candidate.</param>
    /// <returns>Async test.</returns>
    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task WritePageAsyncThrowsWhenRelativePathIsBlank(string relativePath)
    {
        var sink = new CallbackPageSink(static (_, _) => { });
        await Assert.That(() => sink.WritePageAsync(relativePath, new StringBuilder("x"), CancellationToken.None)).Throws<ArgumentException>();
    }

    /// <summary><c>WritePageAsync</c> rejects a <see langword="null"/> builder.</summary>
    /// <returns>Async test.</returns>
    [Test]
    public async Task WritePageAsyncThrowsWhenBuilderIsNull()
    {
        var sink = new CallbackPageSink(static (_, _) => { });
        await Assert.That(() => sink.WritePageAsync("p.md", null!, CancellationToken.None)).Throws<ArgumentNullException>();
    }
}
