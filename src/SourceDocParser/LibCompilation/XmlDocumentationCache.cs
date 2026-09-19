// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;

namespace SourceDocParser.LibCompilation;

/// <summary>Reuses unchanged assembly documentation within one metadata extraction.</summary>
internal sealed class XmlDocumentationCache : IDisposable
{
    /// <summary>Limits conservative retained-document accounting per extraction.</summary>
    private const long DefaultByteBudget = 1024L * 1024 * 1024;

    /// <summary>Allows for decoded text, member names, and index storage.</summary>
    private const int ContentCostMultiplier = 8;

    /// <summary>Allows for the initial member index and cache entry overhead.</summary>
    private const int EntryOverhead = 65_536;

    /// <summary>Accommodates a typical reference pack without repeated lookup growth.</summary>
    private const int InitialEntryCapacity = 256;

    /// <summary>Protects entry ownership and retained-document accounting.</summary>
    private readonly Lock _gate = new();

    /// <summary>Documentation retained for each exact XML asset path.</summary>
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = [with(InitialEntryCapacity, StringComparer.Ordinal)];

    /// <summary>Documentation ordered by its most recent use.</summary>
    private readonly LinkedList<Entry> _recency = new();

    /// <summary>Maximum estimated size of retained documentation.</summary>
    private readonly long _byteBudget;

    /// <summary>Estimated size of the retained entries.</summary>
    private long _retainedBytes;

    /// <summary>Whether extraction has released this cache.</summary>
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="XmlDocumentationCache"/> class.</summary>
    internal XmlDocumentationCache()
        : this(DefaultByteBudget)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="XmlDocumentationCache"/> class.</summary>
    /// <param name="byteBudget">Maximum estimated size of retained documentation.</param>
    internal XmlDocumentationCache(long byteBudget)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteBudget);
        _byteBudget = byteBudget;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _entries.Clear();
            _recency.Clear();
            _retainedBytes = 0;
        }
    }

    /// <summary>Gets the current documentation for an assembly without sharing compilation state.</summary>
    /// <param name="assemblyPath">The exact assembly asset path, including its package version and framework.</param>
    /// <param name="logger">Destination for documentation load diagnostics.</param>
    /// <returns>The assembly's documentation, or null when it is missing or unreadable.</returns>
    internal DocumentationProvider? Get(string assemblyPath, ILogger logger)
    {
        try
        {
            return GetCore(assemblyPath, logger);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return XmlDocsLoader.TryLoad(assemblyPath, logger);
        }
    }

    /// <summary>Reuses documentation only while its file identity remains unchanged.</summary>
    /// <param name="assemblyPath">Assembly whose XML documentation is needed.</param>
    /// <param name="logger">Destination for documentation load diagnostics.</param>
    /// <returns>The current documentation provider, or null when loading fails.</returns>
    private DocumentationProvider? GetCore(string assemblyPath, ILogger logger)
    {
        var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
        var file = new FileInfo(xmlPath);
        if (!file.Exists)
        {
            return null;
        }

        var length = file.Length;
        var stamp = file.LastWriteTimeUtc.Ticks;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.TryGetValue(xmlPath, out var node))
            {
                if (node.Value.Length == length && node.Value.LastWriteTimeTicks == stamp)
                {
                    _recency.Remove(node);
                    _recency.AddFirst(node);
                    return node.Value.Provider;
                }

                Remove(node);
            }
        }

        var provider = XmlDocsLoader.TryLoad(assemblyPath, logger);
        if (provider is null || length > (_byteBudget - EntryOverhead) / ContentCostMultiplier)
        {
            return provider;
        }

        var cost = (length * ContentCostMultiplier) + EntryOverhead;
        Retain(new(xmlPath, length, stamp, cost, provider));
        return provider;
    }

    /// <summary>Retains documentation while the extraction and its size budget allow it.</summary>
    /// <param name="entry">Loaded documentation to retain.</param>
    private void Retain(in Entry entry)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_entries.TryGetValue(entry.Path, out var existing))
            {
                Remove(existing);
            }

            while (_retainedBytes > _byteBudget - entry.Cost && _recency.Last is { } oldest)
            {
                Remove(oldest);
            }

            var node = _recency.AddFirst(entry);
            _entries.Add(entry.Path, node);
            _retainedBytes += entry.Cost;
        }
    }

    /// <summary>Releases a retained documentation entry.</summary>
    /// <param name="node">Entry to remove.</param>
    private void Remove(LinkedListNode<Entry> node)
    {
        _ = _entries.Remove(node.Value.Path);
        _recency.Remove(node);
        _retainedBytes -= node.Value.Cost;
    }

    /// <summary>Documentation associated with one unchanged XML asset.</summary>
    /// <param name="Path">Exact XML asset path.</param>
    /// <param name="Length">File length at load time.</param>
    /// <param name="LastWriteTimeTicks">File modification time at load time.</param>
    /// <param name="Cost">Estimated retained size.</param>
    /// <param name="Provider">Loaded documentation.</param>
    private readonly record struct Entry(string Path, long Length, long LastWriteTimeTicks, long Cost, DocumentationProvider Provider);
}
