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
    /// <summary>NuGet's nuspec namespace from the v2010/07 package schema (matches every modern nuspec we encounter).</summary>
    private const string NuspecNamespace2007 = "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd";

    /// <summary>Older nuspec namespace from the v2011/08 schema, still seen on legacy packages.</summary>
    private const string NuspecNamespace2011 = "http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd";

    /// <summary>Older nuspec namespace from the v2011/10 schema.</summary>
    private const string NuspecNamespace2012 = "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd";

    /// <summary>Local-name of the dependency element regardless of namespace.</summary>
    private const string DependencyElementName = "dependency";

    /// <summary>Attribute name carrying the dependency package id.</summary>
    private const string IdAttributeName = "id";

    /// <summary>
    /// Returns the set of dependency package IDs declared anywhere in
    /// the nupkg's nuspec, deduplicated across TFM groups. Stream-only
    /// — no XDocument materialisation, no per-element allocation
    /// beyond the IDs themselves and the returned set.
    /// </summary>
    /// <param name="nupkgPath">Absolute path to the .nupkg on disk.</param>
    /// <returns>Distinct dependency IDs as an OrdinalIgnoreCase set.</returns>
    public static HashSet<string> ReadDependencyIds(string nupkgPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nupkgPath);
        using var archive = ZipFile.OpenRead(nupkgPath);
        var nuspec = FindNuspecEntry(archive);
        if (nuspec is null)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }

        using var stream = nuspec.Open();
        return ReadDependencyIds(stream);
    }

    /// <summary>
    /// Stream-based overload — useful for tests that want to feed
    /// canned nuspec XML without zipping it first. Reads dependency
    /// elements regardless of which nuspec schema namespace the file
    /// declares.
    /// </summary>
    /// <param name="nuspecStream">Open stream positioned at the start of the nuspec XML.</param>
    /// <returns>Distinct dependency IDs as an OrdinalIgnoreCase set.</returns>
    public static HashSet<string> ReadDependencyIds(Stream nuspecStream)
    {
        ArgumentNullException.ThrowIfNull(nuspecStream);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var settings = new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreWhitespace = true,
            DtdProcessing = DtdProcessing.Prohibit,
        };

        using var reader = XmlReader.Create(nuspecStream, settings);
        while (reader.Read())
        {
            if (reader is { NodeType: XmlNodeType.Element } && IsDependencyElement(reader))
            {
                var id = reader.GetAttribute(IdAttributeName);
                if (!string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
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
        if (!entryName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return entryName.IndexOf('/') < 0 && entryName.IndexOf('\\') < 0;
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
        return ns.Length == 0
            || ns.Equals(NuspecNamespace2007, StringComparison.Ordinal)
            || ns.Equals(NuspecNamespace2011, StringComparison.Ordinal)
            || ns.Equals(NuspecNamespace2012, StringComparison.Ordinal);
    }
}
