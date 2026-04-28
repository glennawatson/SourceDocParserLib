// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>
/// Bridges <see cref="KnownFrameworkPackageMap"/> into the
/// <see cref="NuGetFetcher"/> transitive walk. Some packages reference
/// "synthetic" framework projection assemblies whose nuspec dependencies
/// don't list the NuGet package that ships them -- e.g. a ReactiveUI.Maui
/// DLL references <c>Microsoft.WinUI</c> directly, but the WinUI types
/// live in the <c>Microsoft.WindowsAppSDK</c> NuGet package which the
/// nuspec doesn't surface. The transitive walker would otherwise miss
/// it and the resolver later logs the assembly as unresolved.
///
/// This helper reads every extracted DLL via
/// <see cref="System.Reflection.Metadata.MetadataReader"/>, projects
/// each assembly reference through the synthetic-ref → NuGet-id map,
/// and returns the set of package IDs the fetcher should pull in
/// next. Reads only metadata -- no Roslyn / ICSharpCode dependency.
/// </summary>
internal static class SyntheticPackageBackfill
{
    /// <summary>Glob used when enumerating extracted DLLs -- the per-TFM lib trees only contain managed assemblies.</summary>
    private const string DllPattern = "*.dll";

    /// <summary>
    /// Scans every <c>.dll</c> under <paramref name="libDir"/>'s
    /// per-TFM subdirectories for assembly references whose simple
    /// name lives in <see cref="KnownFrameworkPackageMap"/>, and
    /// returns the corresponding NuGet package IDs minus any already
    /// fetched (tracked in <paramref name="seenIds"/>).
    ///
    /// Pure read-only -- never mutates <paramref name="seenIds"/>;
    /// the caller is responsible for marking the returned IDs as
    /// queued before the next BFS round.
    /// </summary>
    /// <param name="libDir">Per-TFM lib output root the fetcher extracts into.</param>
    /// <param name="seenIds">Package IDs already fetched / queued; never modified by this method.</param>
    /// <returns>De-duplicated package IDs to schedule for the next fetch round.</returns>
    public static List<string> DiscoverFromExtractedAssemblies(string libDir, HashSet<string> seenIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libDir);
        ArgumentNullException.ThrowIfNull(seenIds);

        if (!Directory.Exists(libDir))
        {
            return [];
        }

        var ordered = new List<string>();
        var localSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dllPaths = SafeEnumerateAssemblies(libDir);
        for (var i = 0; i < dllPaths.Length; i++)
        {
            CollectFromAssembly(dllPaths[i], seenIds, localSeen, ordered);
        }

        return ordered;
    }

    /// <summary>
    /// Reads every assembly reference declared in <paramref name="dllPath"/>'s
    /// metadata and, for each one whose simple name maps to a NuGet
    /// package via <see cref="KnownFrameworkPackageMap.TryGetPackageId"/>,
    /// appends the package ID to <paramref name="ordered"/> when it
    /// has not been seen by the caller and is new to this scan.
    /// Silently ignores files that aren't managed assemblies.
    /// </summary>
    /// <param name="dllPath">Absolute path to the candidate DLL.</param>
    /// <param name="alreadyFetched">Caller-owned set of package IDs already fetched / queued.</param>
    /// <param name="localSeen">Per-scan dedupe set so we only emit each package ID once.</param>
    /// <param name="ordered">Output list, mutated in place.</param>
    private static void CollectFromAssembly(string dllPath, HashSet<string> alreadyFetched, HashSet<string> localSeen, List<string> ordered)
    {
        try
        {
            using var stream = File.OpenRead(dllPath);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
            {
                return;
            }

            var reader = pe.GetMetadataReader();
            foreach (var refHandle in reader.AssemblyReferences)
            {
                AppendIfMappedAndNew(reader, refHandle, alreadyFetched, localSeen, ordered);
            }
        }
        catch (BadImageFormatException)
        {
            // Native or malformed PE -- skip silently. Same posture as
            // PublicSurfaceProbe; the resolver's name-based fallback
            // picks up anything we miss here.
        }
        catch (IOException)
        {
            // Concurrent extraction may briefly hold the file. The
            // walker drives extraction itself so retry isn't needed.
        }
    }

    /// <summary>
    /// Resolves <paramref name="refHandle"/>'s simple name and appends
    /// its mapped NuGet package ID when the mapping exists, the ID
    /// hasn't been fetched, and we haven't emitted it earlier in
    /// this scan.
    /// </summary>
    /// <param name="reader">Metadata reader for the containing PE.</param>
    /// <param name="refHandle">Assembly-reference handle to inspect.</param>
    /// <param name="alreadyFetched">Caller-owned dedupe set.</param>
    /// <param name="localSeen">Per-scan dedupe set.</param>
    /// <param name="ordered">Output list, mutated in place.</param>
    private static void AppendIfMappedAndNew(
        MetadataReader reader,
        AssemblyReferenceHandle refHandle,
        HashSet<string> alreadyFetched,
        HashSet<string> localSeen,
        List<string> ordered)
    {
        var name = reader.GetString(reader.GetAssemblyReference(refHandle).Name);
        if (KnownFrameworkPackageMap.TryGetPackageId(name) is not { } packageId)
        {
            return;
        }

        if (alreadyFetched.Contains(packageId) || !localSeen.Add(packageId))
        {
            return;
        }

        ordered.Add(packageId);
    }

    /// <summary>
    /// Wraps <see cref="Directory.GetFiles(string, string, SearchOption)"/>
    /// with permission/IO error suppression so a single unreadable
    /// subdirectory under <c>libDir</c> doesn't break the whole scan.
    /// </summary>
    /// <param name="libDir">Root directory.</param>
    /// <returns>The discovered DLL paths, or an empty array on error.</returns>
    private static string[] SafeEnumerateAssemblies(string libDir)
    {
        try
        {
            return Directory.GetFiles(libDir, DllPattern, SearchOption.AllDirectories);
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }
}
