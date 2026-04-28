// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>
/// Stream decorator that also disposes a paired owner resource
/// (typically the HttpResponseMessage the stream came from) when
/// the stream itself is disposed -- lets callers consume the
/// content with a single <c>using</c> without leaking the owner.
/// Lifted out of the HTTP client so the disposal contract can be
/// exercised against a plain in-memory stream + tracking owner.
/// </summary>
internal sealed class OwningStream : Stream
{
    /// <summary>Underlying stream every read/write delegates to.</summary>
    private readonly Stream _inner;

    /// <summary>Resource disposed when the stream is disposed.</summary>
    private readonly IDisposable _owner;

    /// <summary>Initializes a new instance of the <see cref="OwningStream"/> class.</summary>
    /// <param name="inner">Underlying stream to delegate to.</param>
    /// <param name="owner">Resource disposed alongside the stream.</param>
    public OwningStream(Stream inner, IDisposable owner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(owner);
        _inner = inner;
        _owner = owner;
    }

    /// <inheritdoc />
    public override bool CanRead => _inner.CanRead;

    /// <inheritdoc />
    public override bool CanSeek => _inner.CanSeek;

    /// <inheritdoc />
    public override bool CanWrite => _inner.CanWrite;

    /// <inheritdoc />
    public override long Length => _inner.Length;

    /// <inheritdoc />
    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    /// <inheritdoc />
    public override void Flush() => _inner.Flush();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    /// <inheritdoc />
    public override int Read(Span<byte> buffer) => _inner.Read(buffer);

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _inner.ReadAsync(buffer, offset, count, cancellationToken);

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _inner.ReadAsync(buffer, cancellationToken);

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    /// <inheritdoc />
    public override void SetLength(long value) => _inner.SetLength(value);

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync().ConfigureAwait(false);
        _owner.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
            _owner.Dispose();
        }

        base.Dispose(disposing);
    }
}
