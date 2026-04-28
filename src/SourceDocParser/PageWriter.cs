// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Buffers;
using System.Text;

namespace SourceDocParser;

/// <summary>
/// Writes a <see cref="StringBuilder"/> to disk as UTF-8 by encoding
/// each chunk into a pooled byte buffer and flushing it through an
/// unbuffered <see cref="FileStream"/>. The whole-page string and the
/// 64 KB BufferedFileStreamStrategy buffer never need to exist.
/// </summary>
internal static class PageWriter
{
    /// <summary>Page-level encoder; reused across writes to avoid per-call setup.</summary>
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes <paramref name="sb"/> to <paramref name="path"/> as UTF-8.
    /// Creates the destination directory when missing.
    /// </summary>
    /// <param name="path">Destination path.</param>
    /// <param name="sb">Page contents.</param>
    public static void WriteUtf8(string path, StringBuilder sb)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(sb);

        EnsureDirectory(path);
        using var stream = OpenUnbuffered(path);
        WriteChunks(stream, sb);
    }

    /// <summary>
    /// Async variant of <see cref="WriteUtf8(string, StringBuilder)"/>
    /// for emit pipelines that already run inside a Task chain.
    /// </summary>
    /// <param name="path">Destination path.</param>
    /// <param name="sb">Page contents.</param>
    /// <param name="cancellationToken">Honoured between chunk writes.</param>
    /// <returns>A task representing the asynchronous write.</returns>
    public static async Task WriteUtf8Async(string path, StringBuilder sb, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(sb);

        EnsureDirectory(path);
        await using var stream = OpenUnbufferedAsynchronously(path);
        await WriteChunksAsync(stream, sb, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates the parent directory for <paramref name="path"/> when missing.</summary>
    /// <param name="path">Destination file path.</param>
    private static void EnsureDirectory(string path)
    {
        if (Path.GetDirectoryName(path) is not { Length: > 0 } directory)
        {
            return;
        }

        Directory.CreateDirectory(directory);
    }

    /// <summary>Opens an unbuffered file stream so the only allocations are the OS handle plus the bytes we explicitly write.</summary>
    /// <param name="path">Destination path.</param>
    /// <returns>An open writable file stream.</returns>
    private static FileStream OpenUnbuffered(string path) =>
        new(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1, FileOptions.SequentialScan);

    /// <summary>Async-mode equivalent of <see cref="OpenUnbuffered(string)"/>.</summary>
    /// <param name="path">Destination path.</param>
    /// <returns>An open writable file stream.</returns>
    private static FileStream OpenUnbufferedAsynchronously(string path) =>
        new(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan);

    /// <summary>Writes each <see cref="StringBuilder"/> chunk to <paramref name="stream"/> via a pooled byte buffer.</summary>
    /// <param name="stream">Open file stream.</param>
    /// <param name="sb">Page contents to drain.</param>
    private static void WriteChunks(FileStream stream, StringBuilder sb)
    {
        var encoder = Utf8NoBom.GetEncoder();
        foreach (var chunk in sb.GetChunks())
        {
            var byteCount = Utf8NoBom.GetMaxByteCount(chunk.Length);
            var buffer = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                var written = encoder.GetBytes(chunk.Span, buffer, flush: false);
                stream.Write(buffer, 0, written);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    /// <summary>Async equivalent of <see cref="WriteChunks(FileStream, StringBuilder)"/>.</summary>
    /// <param name="stream">Open file stream.</param>
    /// <param name="sb">Page contents to drain.</param>
    /// <param name="cancellationToken">Honoured between chunk writes.</param>
    /// <returns>A task representing the asynchronous write.</returns>
    private static async Task WriteChunksAsync(FileStream stream, StringBuilder sb, CancellationToken cancellationToken)
    {
        var encoder = Utf8NoBom.GetEncoder();
        foreach (var chunk in sb.GetChunks())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var byteCount = Utf8NoBom.GetMaxByteCount(chunk.Length);
            var buffer = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                var written = encoder.GetBytes(chunk.Span, buffer, flush: false);
                await stream.WriteAsync(buffer.AsMemory(0, written), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
