// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.IO.Compression;
using System.Reflection.PortableExecutable;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>
/// Extracts managed-only DLL entries from a NuGet package.
/// </summary>
/// <remarks>
/// This helper identifies IL-only assemblies within a .nupkg archive,
/// typically under the ref/ directory. It uses PEReader to verify that
/// the executable is a pure managed assembly (IL-only), filtering out
/// native and mixed-mode DLLs that Roslyn cannot consume as references.
/// </remarks>
internal static class ManagedAssemblyExtractor
{
    /// <summary>
    /// Returns true when the supplied stream contains a pure managed (IL-only) assembly.
    /// Filters out native and mixed-mode DLLs that Roslyn cannot consume as references.
    /// </summary>
    /// <param name="stream">Stream positioned at the start of a candidate PE file.</param>
    /// <returns>True when the assembly is managed and IL-only; otherwise false.</returns>
    public static bool IsManagedAssembly(Stream stream)
    {
        try
        {
            using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            return peReader is { HasMetadata: true, PEHeaders.CorHeader.Flags: var flags }
                   && flags.HasFlag(CorFlags.ILOnly);
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true when the entry is a candidate ZIP entry to extract.
    /// It must have the requested path prefix, a non-empty file name, and end with .dll.
    /// </summary>
    /// <param name="entry">ZIP archive entry under inspection.</param>
    /// <param name="prefix">Path prefix; must already end with /.</param>
    /// <returns>True when the entry is a DLL under the requested prefix.</returns>
    public static bool IsCandidateDllEntry(ZipArchiveEntry entry, string prefix)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!entry.FullName.AsSpan().StartsWith(prefix.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (entry.Name is [])
        {
            return false;
        }

        return entry.Name.AsSpan().EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Iterates the archive's entries, yielding every candidate DLL under the path prefix.
    /// This is a pure enumeration without PE filtering or I/O.
    /// </summary>
    /// <param name="archive">Open the NuGet package archive.</param>
    /// <param name="pathPrefix">Path prefix inside the archive; trailing slash optional.</param>
    /// <returns>Lazy enumeration of matching entries.</returns>
    public static IEnumerable<ZipArchiveEntry> SelectAssemblyEntries(ZipArchive archive, string pathPrefix)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(pathPrefix);

        var prefix = PathSeparatorHelpers.EnsureTrailingForwardSlash(pathPrefix);
        return SelectAssemblyEntriesIterator();

        IEnumerable<ZipArchiveEntry> SelectAssemblyEntriesIterator()
        {
            for (var i = 0; i < archive.Entries.Count; i++)
            {
                var entry = archive.Entries[i];
                if (IsCandidateDllEntry(entry, prefix))
                {
                    yield return entry;
                }
            }
        }
    }
}
