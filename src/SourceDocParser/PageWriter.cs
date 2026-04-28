// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;

namespace SourceDocParser;

/// <summary>
/// Streams a fully-composed <see cref="StringBuilder"/> straight to disk
/// using the builder's chunk enumerator -- skips the intermediate
/// <see cref="StringBuilder.ToString()"/> allocation that
/// <see cref="File.WriteAllText(string, string)"/> would otherwise pay
/// per page. For ~600 pages averaging ~30 KB each that's ~18 MB of
/// transient string churn the GC no longer has to deal with on every
/// emit run.
/// </summary>
internal static class PageWriter
{
    /// <summary>
    /// Buffer size for the <see cref="FileStream"/> the writer wraps.
    /// 64 KB matches the default StreamWriter buffer and is large enough
    /// to absorb most page bodies in one syscall while staying small
    /// enough to keep working-set bounded across thousands of pages.
    /// </summary>
    private const int FileStreamBufferSize = 64 * 1024;

    /// <summary>
    /// Writes <paramref name="sb"/> to <paramref name="path"/> as UTF-8.
    /// Creates the destination directory when missing so callers
    /// don't have to repeat the <see cref="Directory.CreateDirectory(string)"/>
    /// call ahead of every write.
    /// </summary>
    /// <param name="path">Destination path.</param>
    /// <param name="sb">Page contents.</param>
    public static void WriteUtf8(string path, StringBuilder sb)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(sb);

        var directory = Path.GetDirectoryName(path);
        if (directory is { Length: > 0 })
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, FileStreamBufferSize, FileOptions.SequentialScan);
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        foreach (var chunk in sb.GetChunks())
        {
            writer.Write(chunk.Span);
        }
    }

    /// <summary>
    /// Async variant of <see cref="WriteUtf8(string, StringBuilder)"/>
    /// for emit pipelines that already run inside a Task chain
    /// (currently the docfx YAML writer).
    /// </summary>
    /// <param name="path">Destination path.</param>
    /// <param name="sb">Page contents.</param>
    /// <param name="cancellationToken">Honoured between chunk writes.</param>
    /// <returns>A task representing the asynchronous write.</returns>
    public static async Task WriteUtf8Async(string path, StringBuilder sb, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(sb);

        var directory = Path.GetDirectoryName(path);
        if (directory is { Length: > 0 })
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, FileStreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        foreach (var chunk in sb.GetChunks())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
        }
    }
}
