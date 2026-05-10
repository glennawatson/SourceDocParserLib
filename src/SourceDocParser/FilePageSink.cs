// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;

namespace SourceDocParser;

/// <summary>
/// <see cref="IPageSink"/> that materializes pages onto the file system, rooted under a
/// configured directory. Default sink for the legacy file-based emit path.
/// </summary>
public sealed class FilePageSink : IPageSink
{
    /// <summary>Absolute root under which relative page paths are resolved.</summary>
    private readonly string _outputRoot;

    /// <summary>Initializes a new instance of the <see cref="FilePageSink"/> class.</summary>
    /// <param name="outputRoot">Absolute output root; the sink creates it on demand.</param>
    public FilePageSink(string outputRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        _outputRoot = outputRoot;
    }

    /// <inheritdoc />
    public void WritePage(string relativePath, StringBuilder builder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(builder);
        PageWriter.WriteUtf8(Path.Combine(_outputRoot, relativePath), builder);
    }

    /// <inheritdoc />
    public Task WritePageAsync(string relativePath, StringBuilder builder, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(builder);
        return PageWriter.WriteUtf8Async(Path.Combine(_outputRoot, relativePath), builder, cancellationToken);
    }
}
