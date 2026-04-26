// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.IO.Compression;
using System.Xml;

namespace SourceDocParser.NuGet;

/// <summary>
/// Small composable helpers for pulling a <c>.nupkg</c>'s declared
/// dependency package IDs out of its <c>.nuspec</c> manifest. Used by
/// the transitive-closure loop in <c>NuGetFetcher</c> so an umbrella
/// package like <c>Splat</c> automatically pulls in its
/// <c>Splat.Core</c> / <c>Splat.Logging</c> / <c>Splat.Builder</c>
/// siblings — without those, the walker can't follow type-forwards
/// to the real type definitions.
/// </summary>
internal static class NuspecDependencyReader
{
    /// <summary>
    /// Common prefix every nuspec schema URI uses
    /// (<c>2010/07</c>, <c>2011/08</c>, <c>2012/06</c>, <c>2013/05</c>,
    /// any future bump). Prefix-match keeps the reader open to schema
    /// version bumps without losing the ability to reject unrelated
    /// XML payloads dropped into a nupkg by accident.
    /// </summary>
    private const string NuspecNamespacePrefix = "http://schemas.microsoft.com/packaging/";

    /// <summary>Local-name of the dependency element regardless of namespace.</summary>
    private const string DependencyElementName = "dependency";

    /// <summary>Attribute name carrying the dependency package id.</summary>
    private const string IdAttributeName = "id";

    /// <summary>
    /// Reader settings shared across every parse — async on so the
    /// XmlReader can pump from <see cref="ZipArchiveEntry.OpenAsync"/>'s
    /// async stream without the runtime throwing on sync .Read().
    /// </summary>
    private static readonly XmlReaderSettings _readerSettings = new()
    {
        Async = true,
        IgnoreComments = true,
        IgnoreWhitespace = true,
        DtdProcessing = DtdProcessing.Prohibit,
    };

    /// <summary>
    /// Returns the set of dependency package IDs declared anywhere in
    /// the nupkg's nuspec, deduplicated across TFM groups. Uses the
    /// .NET 10 async ZIP APIs (<see cref="ZipFile.OpenReadAsync(string, CancellationToken)"/>
    /// + <see cref="ZipArchiveEntry.OpenAsync(CancellationToken)"/>) so
    /// the entire read is non-blocking and integrates with the
    /// fetcher's async pipeline.
    /// </summary>
    /// <param name="nupkgPath">Absolute path to the .nupkg on disk.</param>
    /// <param name="cancellationToken">Token observed across the open + parse.</param>
    /// <returns>Distinct dependency IDs as an array (dedupe happens internally; callers iterate by index).</returns>
    public static async Task<string[]> ReadDependencyIdsAsync(string nupkgPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nupkgPath);
        var archive = await ZipFile.OpenReadAsync(nupkgPath, cancellationToken).ConfigureAwait(false);
        await using (archive.ConfigureAwait(false))
        {
            var nuspec = FindNuspecEntry(archive);
            if (nuspec is null)
            {
                return [];
            }

            var stream = await nuspec.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                return await ReadDependencyIdsAsync(stream, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Reads dependency IDs straight from a sidecar nuspec file on
    /// disk — written by the fetcher next to each cached nupkg so
    /// the transitive-dep walk doesn't have to re-OpenRead the zip
    /// just to read a few hundred bytes of XML.
    /// </summary>
    /// <param name="nuspecPath">Absolute path to the on-disk .nuspec.</param>
    /// <param name="cancellationToken">Token observed across the parse.</param>
    /// <returns>Distinct dependency IDs as an array.</returns>
    public static async Task<string[]> ReadDependencyIdsFromFileAsync(string nuspecPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nuspecPath);
        var stream = new FileStream(nuspecPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.SequentialScan | FileOptions.Asynchronous);
        await using (stream.ConfigureAwait(false))
        {
            return await ReadDependencyIdsAsync(stream, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stream-based overload — useful for tests that want to feed
    /// canned nuspec XML without zipping it first. Reads dependency
    /// elements regardless of which nuspec schema namespace the file
    /// declares.
    /// </summary>
    /// <param name="nuspecStream">Open stream positioned at the start of the nuspec XML.</param>
    /// <param name="cancellationToken">Token observed across the parse.</param>
    /// <returns>Distinct dependency IDs as an array.</returns>
    public static async Task<string[]> ReadDependencyIdsAsync(Stream nuspecStream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nuspecStream);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var reader = XmlReader.Create(nuspecStream, _readerSettings);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader is { NodeType: XmlNodeType.Element } && IsDependencyElement(reader))
            {
                var id = reader.GetAttribute(IdAttributeName);
                if (!string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id);
                }
            }
        }

        return [.. ids];
    }

    /// <summary>
    /// Returns the first <c>.nuspec</c> entry at the archive root.
    /// NuGet packages always carry exactly one — but defensive against
    /// nested entries: the spec only recognises the root one.
    /// </summary>
    /// <param name="archive">Open NuGet package archive.</param>
    /// <returns>The nuspec entry, or null when none is present.</returns>
    public static ZipArchiveEntry? FindNuspecEntry(ZipArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        for (var i = 0; i < archive.Entries.Count; i++)
        {
            var entry = archive.Entries[i];
            if (IsRootNuspecEntry(entry.FullName))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns true when <paramref name="entryName"/> looks like the
    /// root-level nuspec — case-insensitive <c>.nuspec</c> suffix and
    /// no path separator before it.
    /// </summary>
    /// <param name="entryName">Zip entry FullName to test.</param>
    /// <returns>True when this entry is the package's root nuspec.</returns>
    public static bool IsRootNuspecEntry(string entryName)
    {
        ArgumentNullException.ThrowIfNull(entryName);
        if (entryName is not [.., '.', 'n', 'u', 's', 'p', 'e', 'c'] && !entryName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return entryName.AsSpan().IndexOfAny('/', '\\') < 0;
    }

    /// <summary>
    /// Returns true when the reader is positioned on a
    /// <c>&lt;dependency&gt;</c> element — local-name match, with the
    /// namespace check restricted to the nuspec schemas we recognise
    /// so unrelated XML files dropped into a nupkg don't poison the
    /// dependency set.
    /// </summary>
    /// <param name="reader">Reader positioned on an element.</param>
    /// <returns>True when the element is a recognised nuspec dependency entry.</returns>
    public static bool IsDependencyElement(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (!reader.LocalName.Equals(DependencyElementName, StringComparison.Ordinal))
        {
            return false;
        }

        var ns = reader.NamespaceURI;
        return ns is [] || ns.StartsWith(NuspecNamespacePrefix, StringComparison.Ordinal);
    }
}
