// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDocParser.LibCompilation;
using SourceDocParser.Model;
using SourceDocParser.NuGet.Models;
using SourceDocParser.NuGet.Readers;
using SourceDocParser.Tfm;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>
/// <see cref="IAssemblySource"/> that fetches NuGet packages described
/// by <c>nuget-packages.json</c> into <c>apiPath/lib</c> + <c>apiPath/refs</c>
/// and exposes the extracted assemblies, grouped by TFM, to the parser.
/// </summary>
public sealed class NuGetAssemblySource : IAssemblySource
{
    /// <summary>
    /// Filename of the per-fetch sidecar listing every package id the
    /// fetcher promoted to "primary" (owner-discovered + additionalPackages,
    /// minus exclusions). Lives next to <c>lib/</c> so a stale apiPath
    /// from an older library version is detected by file absence and
    /// the source falls back to the manifest's <c>additionalPackages</c>.
    /// </summary>
    internal const string PrimaryPackagesFileName = ".primary-packages";

    /// <summary>Initial slot count for a per-canonical broadcast scratch array.</summary>
    private const int BroadcastSlotInitialCapacity = 4;

    /// <summary>Growth factor applied when a broadcast scratch array fills up.</summary>
    private const int BroadcastSlotGrowthFactor = 2;

    /// <summary>File pattern used to discover assemblies.</summary>
    private const string DllPattern = "*.dll";

    /// <summary>Sub-directory under <c>apiPath</c> holding extracted package lib/ trees.</summary>
    private const string LibDirName = "lib";

    /// <summary>Sub-directory under <c>apiPath</c> holding extracted reference assemblies.</summary>
    private const string RefsDirName = "refs";

    /// <summary>Repository root containing <c>nuget-packages.json</c>.</summary>
    private readonly string _rootDirectory;

    /// <summary>Destination root for fetched and extracted package assemblies.</summary>
    private readonly string _apiPath;

    /// <summary>Logger forwarded to the fetcher and used for discovery progress.</summary>
    private readonly ILogger _logger;

    /// <summary>Fetcher that materialises the lib/ + refs/ trees this source then walks.</summary>
    private readonly INuGetFetcher _fetcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="NuGetAssemblySource"/> class
    /// using default logging and fetch behavior.
    /// </summary>
    /// <param name="rootDirectory">Repository root containing <c>nuget-packages.json</c>.</param>
    /// <param name="apiPath">Destination root for fetched and extracted package assemblies (typically <c>reactiveui/api</c>).</param>
    public NuGetAssemblySource(string rootDirectory, string apiPath)
        : this(rootDirectory, apiPath, null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NuGetAssemblySource"/> class
    /// using the supplied logger and default fetch behavior.
    /// </summary>
    /// <param name="rootDirectory">Repository root containing <c>nuget-packages.json</c>.</param>
    /// <param name="apiPath">Destination root for fetched and extracted package assemblies (typically <c>reactiveui/api</c>).</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public NuGetAssemblySource(string rootDirectory, string apiPath, ILogger? logger)
        : this(rootDirectory, apiPath, logger, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NuGetAssemblySource"/> class.
    /// </summary>
    /// <param name="rootDirectory">Repository root containing <c>nuget-packages.json</c>.</param>
    /// <param name="apiPath">Destination root for fetched and extracted package assemblies (typically <c>reactiveui/api</c>).</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    /// <param name="fetcher">Optional fetcher; defaults to a fresh <see cref="NuGetFetcher"/>. Inject for tests that want to skip the network.</param>
    /// <exception cref="ArgumentException">When <paramref name="rootDirectory"/> or <paramref name="apiPath"/> is null, empty, or whitespace.</exception>
    public NuGetAssemblySource(string rootDirectory, string apiPath, ILogger? logger, INuGetFetcher? fetcher)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiPath);
        _rootDirectory = rootDirectory;
        _apiPath = apiPath;
        _logger = logger ?? NullLogger.Instance;
        _fetcher = fetcher ?? new NuGetFetcher();
    }

    /// <summary>
    /// Fetches the configured packages when needed, then discovers the extracted assemblies.
    /// </summary>
    /// <returns>An async stream of assembly groups keyed by TFM.</returns>
    public IAsyncEnumerable<AssemblyGroup> DiscoverAsync() =>
        DiscoverAsync(CancellationToken.None);

    /// <inheritdoc />
    public async IAsyncEnumerable<AssemblyGroup> DiscoverAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await _fetcher.FetchPackagesAsync(_rootDirectory, _apiPath, _logger, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var libDir = Path.Combine(_apiPath, LibDirName);
        var refsDir = Path.Combine(_apiPath, RefsDirName);

        if (!Directory.Exists(libDir))
        {
            throw new DirectoryNotFoundException($"No {LibDirName}/ directory found at {libDir}");
        }

        // Prefer the fetcher-written sidecar when present so
        // owner-only manifests still produce documentation pages.
        // Fall back to the manifest only when the sidecar is missing.
        var sidecarPath = Path.Combine(_apiPath, PrimaryPackagesFileName);
        var manifestPath = Path.Combine(_rootDirectory, "nuget-packages.json");
        var primaryPrefixes = ResolvePrimaryPrefixes(sidecarPath, manifestPath);

        var libTfms = DiscoverTfms(libDir);
        var refsTfms = Directory.Exists(refsDir) ? DiscoverTfms(refsDir) : [];
        var refDllNameCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        var probed = ProbeTfms(libTfms, libDir, refsDir, refsTfms, primaryPrefixes, refDllNameCache, cancellationToken);
        var canonicals = SelectCanonicalsAndBroadcasts(probed);

        for (var i = 0; i < canonicals.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return canonicals[i];
        }
    }

    /// <summary>
    /// Selects canonical assemblies and associated broadcast targets from the given list of
    /// probed target framework monikers (TFMs), creating groupings of assemblies and broadcast relationships.
    /// </summary>
    /// <param name="probed">
    /// An array of probed TFMs, each containing details about assembly paths,
    /// fallback mappings, unique identifiers, and ranking information.
    /// </param>
    /// <returns>
    /// An array of <see cref="SourceDocParser.Model.AssemblyGroup"/> instances that represent the
    /// canonical assemblies and their corresponding broadcast TFMs.
    /// </returns>
    internal static AssemblyGroup[] SelectCanonicalsAndBroadcasts(ProbedTfm[] probed)
    {
        ArgumentNullException.ThrowIfNull(probed);
        if (probed.Length is 0)
        {
            return [];
        }

        var ranked = RankProbedDescending(probed);
        var canonicalSlots = new ProbedTfm[probed.Length];
        var broadcastSlots = new string[probed.Length][];
        var broadcastCounts = new int[probed.Length];
        var canonicalCount = AssignCanonicalsAndBroadcasts(ranked, canonicalSlots, broadcastSlots, broadcastCounts);

        return MaterialiseGroups(canonicalSlots, broadcastSlots, broadcastCounts, canonicalCount);
    }

    /// <summary>
    /// Resolves the primary-prefix list for the current discovery run.
    /// Prefers the fetcher-written sidecar so owner-discovered ids
    /// surface alongside additionalPackages; falls back to the
    /// manifest's additionalPackages for hand-populated apiPaths or
    /// older fetchers; returns empty when neither is available so
    /// <see cref="IsPrimaryDll"/> walks every DLL.
    /// </summary>
    /// <param name="sidecarPath">Absolute path to the per-fetch sidecar.</param>
    /// <param name="manifestPath">Absolute path to <c>nuget-packages.json</c>.</param>
    /// <returns>Primary prefixes laid out as bare-id / id+dot pairs.</returns>
    internal static string[] ResolvePrimaryPrefixes(string sidecarPath, string manifestPath)
    {
        if (File.Exists(sidecarPath))
        {
            return BuildPrimaryPrefixesFromIds(ReadPrimaryIdsSidecar(sidecarPath));
        }

        if (File.Exists(manifestPath))
        {
            return BuildPrimaryPrefixes(PackageConfigReader.Read(manifestPath));
        }

        return [];
    }

    /// <summary>
    /// Builds the set of "primary package" prefixes the assembly
    /// source uses to decide which DLLs to walk vs. leave as
    /// compile-only refs. Each primary ID is added in two forms --
    /// the bare ID (matches the umbrella DLL) and the ID + dot
    /// (matches sibling assemblies the umbrella forwards to, e.g.
    /// <c>Splat -> Splat.Core</c>). Returns empty when no IDs are
    /// supplied; in that case <see cref="CollectPackageDlls"/>
    /// falls back to walking everything.
    /// </summary>
    /// <param name="config">Loaded package config.</param>
    /// <returns>Distinct primary prefixes laid out as bare-id / id+dot pairs -- empty when no <c>additionalPackages</c> are declared.</returns>
    internal static string[] BuildPrimaryPrefixes(PackageConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.AdditionalPackages is not [_, ..])
        {
            return [];
        }

        var ids = new string[config.AdditionalPackages.Length];
        for (var i = 0; i < config.AdditionalPackages.Length; i++)
        {
            ids[i] = config.AdditionalPackages[i].Id;
        }

        return BuildPrimaryPrefixesFromIds(ids);
    }

    /// <summary>
    /// Same shape as <see cref="BuildPrimaryPrefixes(PackageConfig)"/>
    /// but driven by the explicit id list the fetcher persists to its
    /// <see cref="PrimaryPackagesFileName"/> sidecar -- that list is
    /// the union of owner-discovered and additionalPackages ids
    /// (after user exclusions and before transitive expansion), which
    /// is the set the user expects documentation pages for.
    /// </summary>
    /// <param name="ids">Primary package identifiers.</param>
    /// <returns>Distinct primary prefixes laid out as bare-id / id+dot pairs -- empty when no usable ids are supplied.</returns>
    internal static string[] BuildPrimaryPrefixesFromIds(string[] ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids is [])
        {
            return [];
        }

        var valid = 0;
        for (var i = 0; i < ids.Length; i++)
        {
            if (ids[i] is { Length: > 0 })
            {
                valid++;
            }
        }

        if (valid is 0)
        {
            return [];
        }

        var prefixes = new string[valid * 2];
        var write = 0;
        for (var i = 0; i < ids.Length; i++)
        {
            var id = ids[i];
            if (id is not [_, ..])
            {
                continue;
            }

            prefixes[write++] = id;
            prefixes[write++] = id + ".";
        }

        return prefixes;
    }

    /// <summary>
    /// Reads the fetcher-written <see cref="PrimaryPackagesFileName"/>
    /// sidecar -- one package id per line, blank lines and lines
    /// starting with <c>#</c> ignored so callers can hand-edit if
    /// they need to. Missing-file callers should pre-check existence;
    /// this helper assumes the file is present.
    /// </summary>
    /// <param name="sidecarPath">Absolute path to the sidecar.</param>
    /// <returns>The trimmed, non-comment ids in declaration order.</returns>
    internal static string[] ReadPrimaryIdsSidecar(string sidecarPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sidecarPath);
        var lines = File.ReadAllLines(sidecarPath);

        // Two-pass count-then-fill so the result lands in an
        // exact-sized array -- sidecar lines are short so the second
        // pass costs nothing meaningful.
        var valid = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].AsSpan().Trim();
            if (trimmed.Length is not 0 && trimmed[0] is not '#')
            {
                valid++;
            }
        }

        if (valid is 0)
        {
            return [];
        }

        var ids = new string[valid];
        var write = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].AsSpan().Trim();
            if (line.Length is 0 || line[0] is '#')
            {
                continue;
            }

            ids[write++] = line.ToString();
        }

        return ids;
    }

    /// <summary>
    /// Returns true when <paramref name="dllNameWithoutExt"/> looks
    /// like it came from one of the user's primary packages -- exact
    /// match against the bare ID, or starts with <c>id + "."</c>.
    /// When <paramref name="primaryPrefixes"/> is empty the source
    /// hasn't been told what's primary (e.g. owner-discovery only,
    /// or no manifest); fall back to walking everything in lib/ so
    /// behaviour matches the pre-filter version of the source.
    /// </summary>
    /// <param name="dllNameWithoutExt">DLL filename minus extension.</param>
    /// <param name="primaryPrefixes">Primary prefixes from <see cref="BuildPrimaryPrefixes"/>.</param>
    /// <returns>True when the DLL should be walked for documentation pages.</returns>
    internal static bool IsPrimaryDll(string dllNameWithoutExt, string[] primaryPrefixes)
    {
        ArgumentNullException.ThrowIfNull(dllNameWithoutExt);
        ArgumentNullException.ThrowIfNull(primaryPrefixes);
        if (primaryPrefixes is not [_, ..])
        {
            return true;
        }

        var dllNameSpan = dllNameWithoutExt.AsSpan();
        for (var i = 0; i < primaryPrefixes.Length; i++)
        {
            var prefix = primaryPrefixes[i];

            // Even-indexed entries are bare IDs (exact match); odd-indexed
            // are "<id>." (prefix match for sibling assemblies). The
            // BuildPrimaryPrefixes layout pairs them so this single loop
            // handles both shapes without a second pass.
            if ((i & 1) == 0
                ? dllNameSpan.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                : dllNameSpan.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the names of every immediate sub-directory of <paramref name="root"/> that contains at least one DLL.
    /// </summary>
    /// <param name="root">Directory to enumerate.</param>
    /// <returns>Sorted list of TFM directory names.</returns>
    internal static List<string> DiscoverTfms(string root)
    {
        var tfms = new List<string>();

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            if (HasAtLeastOneFile(dir, DllPattern))
            {
                tfms.Add(Path.GetFileName(dir.AsSpan()).ToString());
            }
        }

        tfms.Sort(StringComparer.Ordinal);
        return tfms;
    }

    /// <summary>
    /// Returns the package DLLs in <paramref name="libTfmDir"/> the
    /// walker should visit -- excludes co-located reference assemblies
    /// (those are compile-only) and, when <paramref name="primaryPrefixes"/>
    /// has entries, restricts the result to DLLs whose filename
    /// matches one of the user's primary packages. Transitive deps
    /// stay on disk for the compilation's fallback index but don't
    /// produce documentation pages.
    /// </summary>
    /// <param name="libTfmDir">Per-TFM lib directory.</param>
    /// <param name="refDllNames">Filename-without-extension names of reference assemblies to exclude.</param>
    /// <param name="primaryPrefixes">Bare-id / id+dot pairs from <see cref="BuildPrimaryPrefixes"/>; empty means no filter.</param>
    /// <returns>Sorted list of absolute DLL paths.</returns>
    internal static List<string> CollectPackageDlls(string libTfmDir, HashSet<string> refDllNames, string[] primaryPrefixes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libTfmDir);
        ArgumentNullException.ThrowIfNull(refDllNames);
        ArgumentNullException.ThrowIfNull(primaryPrefixes);

        var packageDlls = new List<string>();

        foreach (var dll in Directory.EnumerateFiles(libTfmDir, DllPattern))
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(dll.AsSpan()).ToString();
            if (refDllNames.Contains(nameWithoutExt))
            {
                continue;
            }

            if (!IsPrimaryDll(nameWithoutExt, primaryPrefixes))
            {
                continue;
            }

            packageDlls.Add(dll);
        }

        packageDlls.Sort(StringComparer.Ordinal);
        return packageDlls;
    }

    /// <summary>
    /// Returns the cached reference-assembly names for <paramref name="bestRef"/>,
    /// populating the cache on the first request.
    /// </summary>
    /// <param name="cache">Per-discovery cache keyed by refs/ TFM.</param>
    /// <param name="refsDir">Root refs directory.</param>
    /// <param name="bestRef">Matched refs TFM.</param>
    /// <returns>Filename-without-extension names of reference assemblies for the TFM.</returns>
    internal static HashSet<string> GetOrAddRefDllNames(
        Dictionary<string, HashSet<string>> cache,
        string refsDir,
        string bestRef)
    {
        if (cache.TryGetValue(bestRef, out var cached))
        {
            return cached;
        }

        var names = CollectFileNamesWithoutExtension(Path.Combine(refsDir, bestRef), DllPattern);
        cache[bestRef] = names;
        return names;
    }

    /// <summary>
    /// Returns the filenames-without-extension of every file matching <paramref name="pattern"/> in <paramref name="dir"/>.
    /// </summary>
    /// <param name="dir">Directory to scan (must exist).</param>
    /// <param name="pattern">File pattern.</param>
    /// <returns>OrdinalIgnoreCase set of filenames without extensions.</returns>
    internal static HashSet<string> CollectFileNamesWithoutExtension(string dir, string pattern)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(dir, pattern))
        {
            names.Add(Path.GetFileNameWithoutExtension(file.AsSpan()).ToString());
        }

        return names;
    }

    /// <summary>
    /// Builds the ordered directory list the fallback assembly index
    /// scans for the supplied <paramref name="targetTfm"/>. The list is
    /// (in order) the refs/ dir for the best matching ref TFM (if any),
    /// then the target TFM's own <c>lib/</c> dir, then every other
    /// runtime-compatible TFM's <c>lib/</c> dir from highest to lowest
    /// rank. Compatible-TFM fallbacks pick up transitive dependencies
    /// (e.g. <c>System.Reactive</c> shipped under <c>netstandard2.0</c>)
    /// that the consuming assembly references but that
    /// <see cref="TfmResolver.SelectAllSupportedTfms"/> didn't extract
    /// into the consumer's TFM bucket -- without this the assembly
    /// resolver logs <c>Unable to resolve assembly reference</c> warnings
    /// for every transitively-pulled dep that targets a lower TFM.
    /// </summary>
    /// <param name="libDir">Absolute <c>lib/</c> root.</param>
    /// <param name="libTfms">TFM directory names under <c>lib/</c>.</param>
    /// <param name="targetTfm">The TFM bucket currently being probed.</param>
    /// <param name="libTfmDir">Absolute path to <paramref name="targetTfm"/>'s lib dir.</param>
    /// <param name="refsDir">Absolute <c>refs/</c> root.</param>
    /// <param name="bestRefTfm">Pre-resolved best matching ref TFM, or empty when no ref pack matches.</param>
    /// <param name="sdkRefPackDirs">
    /// Optional pre-discovered SDK ref-pack directories (highest priority first within the SDK tier).
    /// Pass an empty list to disable SDK fallback. Tests pass synthetic dirs; production passes
    /// the result of <see cref="RefPackProbe.ProbeRefPackRefDirs"/>.
    /// </param>
    /// <returns>The directory list in scan order.</returns>
    internal static List<string> BuildFallbackDirList(
        string libDir,
        List<string> libTfms,
        string targetTfm,
        string libTfmDir,
        string refsDir,
        string? bestRefTfm,
        IReadOnlyList<string> sdkRefPackDirs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libDir);
        ArgumentNullException.ThrowIfNull(libTfms);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTfm);
        ArgumentException.ThrowIfNullOrWhiteSpace(libTfmDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(refsDir);
        ArgumentNullException.ThrowIfNull(sdkRefPackDirs);

        var compatible = TfmResolver.SelectCompatibleTfms(targetTfm, libTfms);

        // refs first (when present), then target lib dir, then every
        // compatible lib dir except the target itself, then any SDK
        // ref-pack dirs the locator turned up. SDK packs go last so
        // a DLL shipped in the consumer's own lib/ always wins on
        // duplicate names; SDK refs only fill in gaps for synthetic
        // platform/workload assemblies.
        var capacity = compatible.Count + (bestRefTfm is [_, ..] ? 1 : 0) + 1 + sdkRefPackDirs.Count;
        var dirs = new List<string>(capacity);
        if (bestRefTfm is [_, ..])
        {
            dirs.Add(Path.Combine(refsDir, bestRefTfm));
        }

        dirs.Add(libTfmDir);
        for (var i = 0; i < compatible.Count; i++)
        {
            var tfm = compatible[i];
            if (string.Equals(tfm, targetTfm, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            dirs.Add(Path.Combine(libDir, tfm));
        }

        for (var i = 0; i < sdkRefPackDirs.Count; i++)
        {
            dirs.Add(sdkRefPackDirs[i]);
        }

        return dirs;
    }

    /// <summary>
    /// Sorts <paramref name="probed"/> by descending TFM rank with an
    /// ordinal tiebreaker so the canonical pick is deterministic.
    /// </summary>
    /// <param name="probed">Source array (left untouched).</param>
    /// <returns>A new array holding the sorted entries.</returns>
    private static ProbedTfm[] RankProbedDescending(ProbedTfm[] probed)
    {
        var ranked = new ProbedTfm[probed.Length];
        Array.Copy(probed, ranked, probed.Length);
        Array.Sort(ranked, static (a, b) => a.Rank == b.Rank
            ? string.CompareOrdinal(a.Tfm, b.Tfm)
            : b.Rank.CompareTo(a.Rank));

        return ranked;
    }

    /// <summary>
    /// Walks <paramref name="ranked"/> and assigns each entry as a
    /// canonical or as a broadcast target for an existing canonical
    /// whose probed UID set is a superset.
    /// </summary>
    /// <param name="ranked">Rank-sorted probe results.</param>
    /// <param name="canonicalSlots">Pre-sized canonical scratch array; populated in place.</param>
    /// <param name="broadcastSlots">Pre-sized per-canonical broadcast scratch arrays.</param>
    /// <param name="broadcastCounts">Per-canonical broadcast counts; updated in place.</param>
    /// <returns>The number of canonicals identified.</returns>
    private static int AssignCanonicalsAndBroadcasts(
        ProbedTfm[] ranked,
        ProbedTfm[] canonicalSlots,
        string[][] broadcastSlots,
        int[] broadcastCounts)
    {
        var canonicalCount = 0;
        for (var i = 0; i < ranked.Length; i++)
        {
            var probe = ranked[i];
            var matched = FindCanonicalSuperset(canonicalSlots, canonicalCount, probe);
            if (matched < 0)
            {
                canonicalSlots[canonicalCount++] = probe;
                continue;
            }

            AppendBroadcast(broadcastSlots, broadcastCounts, matched, probe.Tfm);
        }

        return canonicalCount;
    }

    /// <summary>Finds the first canonical whose probed UID set is a superset of <paramref name="probe"/>'s.</summary>
    /// <param name="canonicalSlots">Canonical scratch array.</param>
    /// <param name="canonicalCount">How many entries in <paramref name="canonicalSlots"/> are populated.</param>
    /// <param name="probe">Probe whose UID set to test.</param>
    /// <returns>Index of the matching canonical, or -1 when none match.</returns>
    private static int FindCanonicalSuperset(ProbedTfm[] canonicalSlots, int canonicalCount, ProbedTfm probe)
    {
        for (var c = 0; c < canonicalCount; c++)
        {
            if (probe.Uids.IsSubsetOf(canonicalSlots[c].Uids))
            {
                return c;
            }
        }

        return -1;
    }

    /// <summary>Appends <paramref name="tfm"/> to canonical <paramref name="canonicalIndex"/>'s broadcast list, growing the scratch array as needed.</summary>
    /// <param name="broadcastSlots">Per-canonical broadcast scratch arrays.</param>
    /// <param name="broadcastCounts">Per-canonical broadcast counts.</param>
    /// <param name="canonicalIndex">Canonical index to append to.</param>
    /// <param name="tfm">TFM to add as a broadcast target.</param>
    private static void AppendBroadcast(string[][] broadcastSlots, int[] broadcastCounts, int canonicalIndex, string tfm)
    {
        var bcArr = broadcastSlots[canonicalIndex];
        var bcCount = broadcastCounts[canonicalIndex];
        if (bcArr is null)
        {
            bcArr = new string[BroadcastSlotInitialCapacity];
            broadcastSlots[canonicalIndex] = bcArr;
        }
        else if (bcCount == bcArr.Length)
        {
            Array.Resize(ref bcArr, bcArr.Length * BroadcastSlotGrowthFactor);
            broadcastSlots[canonicalIndex] = bcArr;
        }

        bcArr[bcCount] = tfm;
        broadcastCounts[canonicalIndex] = bcCount + 1;
    }

    /// <summary>Materialises the final <see cref="AssemblyGroup"/> array from the per-canonical scratch state.</summary>
    /// <param name="canonicalSlots">Populated canonical scratch array.</param>
    /// <param name="broadcastSlots">Populated per-canonical broadcast scratch arrays.</param>
    /// <param name="broadcastCounts">Per-canonical broadcast counts.</param>
    /// <param name="canonicalCount">How many entries in <paramref name="canonicalSlots"/> are populated.</param>
    /// <returns>One <see cref="AssemblyGroup"/> per canonical.</returns>
    private static AssemblyGroup[] MaterialiseGroups(
        ProbedTfm[] canonicalSlots,
        string[][] broadcastSlots,
        int[] broadcastCounts,
        int canonicalCount)
    {
        var result = new AssemblyGroup[canonicalCount];
        for (var i = 0; i < canonicalCount; i++)
        {
            var bcCount = broadcastCounts[i];
            string[] broadcast;
            if (bcCount is 0)
            {
                broadcast = [];
            }
            else if (bcCount == broadcastSlots[i].Length)
            {
                broadcast = broadcastSlots[i];
            }
            else
            {
                broadcast = new string[bcCount];
                Array.Copy(broadcastSlots[i], broadcast, bcCount);
            }

            result[i] = new(canonicalSlots[i].Tfm, canonicalSlots[i].Dlls, canonicalSlots[i].Fallback, broadcast);
        }

        return result;
    }

    /// <summary>
    /// Cheap "does this directory contain at least one matching file?" check via lazy enumeration.
    /// </summary>
    /// <param name="dir">Directory to check.</param>
    /// <param name="pattern">File pattern.</param>
    /// <returns>True if at least one matching file exists.</returns>
    private static bool HasAtLeastOneFile(string dir, string pattern)
    {
        using var enumerator = Directory.EnumerateFiles(dir, pattern).GetEnumerator();
        return enumerator.MoveNext();
    }

    /// <summary>
    /// Probes every TFM directory's public API surface via
    /// <see cref="PublicSurfaceProbe"/>; returns one
    /// <see cref="ProbedTfm"/> per TFM whose <c>lib/</c> contains at
    /// least one walkable DLL. Empty groups are dropped here so the
    /// caller doesn't need to filter again.
    /// </summary>
    /// <param name="libTfms">TFM directory names under <c>lib/</c>.</param>
    /// <param name="libDir">Absolute <c>lib/</c> root.</param>
    /// <param name="refsDir">Absolute <c>refs/</c> root used for fallback resolution.</param>
    /// <param name="refsTfms">Sorted list of <c>refs/</c> TFM directory names.</param>
    /// <param name="primaryPrefixes">Primary id prefix layout from <see cref="ResolvePrimaryPrefixes"/>.</param>
    /// <param name="refDllNameCache">Per-discovery cache of reference DLL names.</param>
    /// <param name="cancellationToken">Cancellation token honoured between TFMs.</param>
    /// <returns>The probed groups in <paramref name="libTfms"/> order.</returns>
    private ProbedTfm[] ProbeTfms(
        List<string> libTfms,
        string libDir,
        string refsDir,
        List<string> refsTfms,
        string[] primaryPrefixes,
        Dictionary<string, HashSet<string>> refDllNameCache,
        CancellationToken cancellationToken)
    {
        // Probe the locally-installed .NET SDK ref packs once per
        // discovery -- the on-disk layout doesn't change between
        // TFM probes, so we share the result across the loop. Per-
        // TFM filtering happens inside BuildFallbackDirList via
        // RefPackProbe.ProbeRefPackRefDirs.
        var packRoots = DotNetSdkLocator.EnumeratePackRoots();

        var slots = new ProbedTfm[libTfms.Count];
        var count = 0;
        for (var i = 0; i < libTfms.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tfm = libTfms[i];
            var libTfmDir = Path.Combine(libDir, tfm);
            var bestRef = TfmResolver.FindBestRefsTfm(tfm, refsTfms);
            HashSet<string> refDllNames;
            Dictionary<string, string> fallbackIndex;
            var sdkRefDirs = RefPackProbe.ProbeRefPackRefDirs(
                packRoots,
                TfmResolver.SelectCompatibleTfms(tfm, libTfms));
            var fallbackDirs = BuildFallbackDirList(libDir, libTfms, tfm, libTfmDir, refsDir, bestRef, sdkRefDirs);
            if (bestRef is [_, ..])
            {
                refDllNames = GetOrAddRefDllNames(refDllNameCache, refsDir, bestRef);
                fallbackIndex = AssemblyResolution.BuildFallbackIndex(fallbackDirs, _logger);
            }
            else
            {
                refDllNames = new(StringComparer.OrdinalIgnoreCase);
                fallbackIndex = AssemblyResolution.BuildFallbackIndex(fallbackDirs, _logger);
            }

            var packageDlls = CollectPackageDlls(libTfmDir, refDllNames, primaryPrefixes);
            if (packageDlls.Count is 0)
            {
                continue;
            }

            var dlls = packageDlls.ToArray();
            var uids = PublicSurfaceProbe.ProbePublicTypeUids(dlls);
            slots[count++] = new(tfm, dlls, fallbackIndex, uids, Tfm.Tfm.Parse(tfm).Rank);
        }

        if (count == slots.Length)
        {
            return slots;
        }

        var result = new ProbedTfm[count];
        Array.Copy(slots, result, count);
        return result;
    }

    /// <summary>
    /// Per-TFM probe result paired with the metadata the
    /// <see cref="AssemblyGroup"/> needs once a canonical pick is made.
    /// </summary>
    /// <param name="Tfm">TFM directory name.</param>
    /// <param name="Dlls">Absolute paths to the package DLLs in this TFM's <c>lib/</c>.</param>
    /// <param name="Fallback">Resolver fallback index for the compilation.</param>
    /// <param name="Uids">Public type UIDs probed via <see cref="PublicSurfaceProbe"/>.</param>
    /// <param name="Rank">TFM rank.</param>
    internal sealed record ProbedTfm(
        string Tfm,
        string[] Dlls,
        Dictionary<string, string> Fallback,
        HashSet<string> Uids,
        int Rank);
}
