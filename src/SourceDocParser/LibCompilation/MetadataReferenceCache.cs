// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace SourceDocParser.LibCompilation;

/// <summary>
/// Caches Roslyn <see cref="MetadataReference"/> instances by absolute path per TFM.
/// Reusing references avoids redundant BCL ref pack loading and handles XML doc attachment.
/// </summary>
/// <remarks>
/// Each <see cref="PortableExecutableReference"/> is backed by a memory-mapped
/// view of the source DLL via <see cref="AssemblyMetadata"/>. The cache holds
/// onto those views for its lifetime; <see cref="Dispose"/> releases them.
/// Always wrap the cache in <c>using</c> so consumers (especially anything
/// that drives the parser repeatedly, like benchmarks) don't accumulate
/// pinned native memory across runs.
/// </remarks>
internal sealed class MetadataReferenceCache : IDisposable
{
    /// <summary>Map of assembly paths to cached references. Case-insensitive for Windows path variations.</summary>
    private readonly ConcurrentDictionary<string, MetadataReference> _byPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Logger for XML doc load progress and parse failures.</summary>
    private readonly ILogger _logger;

    /// <summary>Whether consumers need the supplied assemblies' XML documentation.</summary>
    private readonly bool _includeXmlDocumentation;

    /// <summary>Initializes a new instance of the <see cref="MetadataReferenceCache"/> class.</summary>
    /// <param name="logger">Logger for XML doc load progress and parse failures.</param>
    public MetadataReferenceCache(ILogger logger)
        : this(logger, true)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MetadataReferenceCache"/> class.</summary>
    /// <param name="logger">Diagnostic destination.</param>
    /// <param name="includeXmlDocumentation">Whether callers need XML documentation.</param>
    public MetadataReferenceCache(ILogger logger, bool includeXmlDocumentation)
    {
        _logger = logger;
        _includeXmlDocumentation = includeXmlDocumentation;
    }

    /// <summary>
    /// Disposes every cached <see cref="MetadataReference"/>'s backing
    /// <see cref="AssemblyMetadata"/>, releasing the memory-mapped DLL view
    /// for each entry. Subsequent <see cref="Get"/> calls would re-load
    /// from disk (callers typically drop the cache itself rather than
    /// re-using a disposed one).
    /// </summary>
    public void Dispose()
    {
        foreach (var (_, entry) in _byPath)
        {
            if (entry is PortableExecutableReference peReference)
            {
                peReference.GetMetadata().Dispose();
            }
        }

        _byPath.Clear();
    }

    /// <summary>Gets or loads the <see cref="MetadataReference"/> for the specified assembly path.</summary>
    /// <param name="assemblyPath">The absolute path to the assembly DLL.</param>
    /// <returns>A metadata reference, possibly with XML documentation attached.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal MetadataReference Get(string assemblyPath) =>
        _byPath.GetOrAdd(
            assemblyPath,
            static (path, state) =>
            {
                var documentation = state.IncludeXmlDocumentation ? XmlDocsLoader.TryLoad(path, state.Logger) : null;
                return documentation is null
                    ? MetadataReference.CreateFromFile(path)
                    : MetadataReference.CreateFromFile(path, documentation: documentation);
            },
            new FactoryState(_logger, _includeXmlDocumentation));

    /// <summary>The state of the Factory.</summary>
    /// <param name="Logger">Logger for XML doc load progress and parse failures.</param>
    /// <param name="IncludeXmlDocumentation">Whether callers need XML documentation.</param>
    private readonly record struct FactoryState(ILogger Logger, bool IncludeXmlDocumentation);
}
