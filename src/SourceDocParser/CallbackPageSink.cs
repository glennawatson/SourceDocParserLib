// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Buffers;
using System.Text;

namespace SourceDocParser;

/// <summary>
/// <see cref="IPageSink"/> that hands each page to a caller-supplied callback as encoded
/// UTF-8 bytes. The bytes are encoded once into a freshly-allocated array so the callback
/// owns them outright (no pool ties, safe to retain).
/// </summary>
public sealed class CallbackPageSink : IPageSink
{
    /// <summary>UTF-8 (no BOM) encoder shared with <see cref="PageWriter"/>.</summary>
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Callback invoked for each page.</summary>
    private readonly Action<string, byte[]> _callback;

    /// <summary>Initializes a new instance of the <see cref="CallbackPageSink"/> class.</summary>
    /// <param name="callback">Receives <c>(relativePath, utf8Bytes)</c> per page; called synchronously inline with the emitter loop, so it must not block.</param>
    public CallbackPageSink(Action<string, byte[]> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
    }

    /// <inheritdoc />
    public void WritePage(string relativePath, StringBuilder builder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(builder);
        _callback(relativePath, Encode(builder));
    }

    /// <inheritdoc />
    public Task WritePageAsync(string relativePath, StringBuilder builder, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WritePage(relativePath, builder);
        return Task.CompletedTask;
    }

    /// <summary>Encodes <paramref name="builder"/> as UTF-8 into a freshly-allocated byte array.</summary>
    /// <param name="builder">Source text.</param>
    /// <returns>UTF-8 bytes; ownership transfers to the callback.</returns>
    private static byte[] Encode(StringBuilder builder)
    {
        var maxBytes = Utf8NoBom.GetMaxByteCount(builder.Length);
        var rented = ArrayPool<byte>.Shared.Rent(maxBytes);
        try
        {
            var encoder = Utf8NoBom.GetEncoder();
            var written = 0;
            foreach (var chunk in builder.GetChunks())
            {
                written += encoder.GetBytes(chunk.Span, rented.AsSpan(written), flush: false);
            }

            written += encoder.GetBytes([], rented.AsSpan(written), flush: true);
            var result = new byte[written];
            rented.AsSpan(0, written).CopyTo(result);
            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
