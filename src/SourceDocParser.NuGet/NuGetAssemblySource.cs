// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;

namespace SourceDocParser.NuGet;

/// <summary>
/// <see cref="IAssemblySource"/> that fetches NuGet packages described
/// by <c>nuget-packages.json</c> into <c>apiPath/lib</c> + <c>apiPath/refs</c>
/// and exposes the extracted assemblies, grouped by TFM, to the parser.
/// </summary>
public sealed class NuGetAssemblySource : IAssemblySource
{
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
    /// Initializes a new instance of the <see cref="NuGetAssemblySource"/> class.
    /// </summary>
    /// <param name="rootDirectory">Repository root containing <c>nuget-packages.json</c>.</param>
    /// <param name="apiPath">Destination root for fetched and extracted package assemblies (typically <c>reactiveui/api</c>).</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    /// <param name="fetcher">Optional fetcher; defaults to a fresh <see cref="NuGetFetcher"/>. Inject for tests that want to skip the network.</param>
    /// <exception cref="ArgumentException">When <paramref name="rootDirectory"/> or <paramref name="apiPath"/> is null, empty, or whitespace.</exception>
    public NuGetAssemblySource(string rootDirectory, string apiPath, ILogger? logger = null, INuGetFetcher? fetcher = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiPath);
        _rootDirectory = rootDirectory;
        _apiPath = apiPath;
        _logger = logger ?? NullLogger.Instance;
        _fetcher = fetcher ?? new NuGetFetcher();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AssemblyGroup> DiscoverAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await _fetcher.FetchPackagesAsync(_rootDirectory, _apiPath, _logger, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var libDir = Path.Combine(_apiPath, LibDirName);
        var refsDir = Path.Combine(_apiPath, RefsDirName);

        if (!Directory.Exists(libDir))
        {
            throw new DirectoryNotFoundException($"No {LibDirName}/ directory found at {libDir}");
        }

        // Re-read the manifest the fetcher used so we know which
        // packages the user explicitly asked for. Transitive deps
        // (Microsoft.Maui.Controls pulled in via CrissCross.MAUI,
        // Xamarin.Google.* pulled in via Maui, etc.) get fetched and
        // stay in the fallback index so compilation resolves them,
        // but they're not walked for documentation pages — those
        // pages are not what the user wanted.
        var manifestPath = Path.Combine(_rootDirectory, "nuget-packages.json");
        var primaryPrefixes = File.Exists(manifestPath)
            ? BuildPrimaryPrefixes(PackageConfigReader.Read(manifestPath))
            : [];

        var libTfms = DiscoverTfms(libDir);
        var refsTfms = Directory.Exists(refsDir) ? DiscoverTfms(refsDir) : [];
        var refDllNameCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < libTfms.Count; i++)
        {
            var tfm = libTfms[i];
            cancellationToken.ThrowIfCancellationRequested();

            var libTfmDir = Path.Combine(libDir, tfm);
            var bestRef = TfmResolver.FindBestRefsTfm(tfm, refsTfms);

            var refDllNames = bestRef is not null
                ? GetOrAddRefDllNames(refDllNameCache, refsDir, bestRef)
                : new(StringComparer.OrdinalIgnoreCase);

            var packageDlls = CollectPackageDlls(libTfmDir, refDllNames, primaryPrefixes);
            if (packageDlls.Count == 0)
            {
                continue;
            }

            var fallbackIndex = bestRef is not null
                ? AssemblyResolution.BuildFallbackIndex([Path.Combine(refsDir, bestRef), libTfmDir], _logger)
                : AssemblyResolution.BuildFallbackIndex([libTfmDir], _logger);

            yield return new(tfm, [.. packageDlls], fallbackIndex);
        }
    }

    /// <summary>
    /// Builds the set of "primary package" prefixes the assembly
    /// source uses to decide which DLLs to walk vs. leave as
    /// compile-only refs. Each primary ID is added in two forms —
    /// the bare ID (matches the umbrella DLL) and the ID + dot
    /// (matches sibling assemblies the umbrella forwards to, e.g.
    /// <c>Splat → Splat.Core</c>). Returns empty when no IDs are
    /// supplied; in that case <see cref="CollectPackageDlls"/>
    /// falls back to walking everything.
    /// </summary>
    /// <param name="config">Loaded package config.</param>
    /// <returns>Distinct primary prefixes laid out as bare-id / id+dot pairs — empty when no <c>additionalPackages</c> are declared.</returns>
    internal static string[] BuildPrimaryPrefixes(PackageConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.AdditionalPackages.Length == 0)
        {
            return [];
        }

        var prefixes = new List<string>(config.AdditionalPackages.Length * 2);
        for (var i = 0; i < config.AdditionalPackages.Length; i++)
        {
            var id = config.AdditionalPackages[i].Id;
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            prefixes.Add(id);
            prefixes.Add(id + ".");
        }

        return [.. prefixes];
    }

    /// <summary>
    /// Returns true when <paramref name="dllNameWithoutExt"/> looks
    /// like it came from one of the user's primary packages — exact
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
        if (primaryPrefixes.Length == 0)
        {
            return true;
        }

        for (var i = 0; i < primaryPrefixes.Length; i++)
        {
            var prefix = primaryPrefixes[i];

            // Even-indexed entries are bare IDs (exact match); odd-indexed
            // are "<id>." (prefix match for sibling assemblies). The
            // BuildPrimaryPrefixes layout pairs them so this single loop
            // handles both shapes without a second pass.
            if ((i & 1) == 0
                ? dllNameWithoutExt.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                : dllNameWithoutExt.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
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
    /// walker should visit — excludes co-located reference assemblies
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
}
