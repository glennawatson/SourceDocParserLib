// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Versioning;
using Polly;
using Polly.Retry;

namespace SourceDocParser.NuGet;

/// <summary>
/// Fetches NuGet packages defined by <c>nuget-packages.json</c>, extracts
/// their managed assemblies into TFM-bucketed directories.
/// </summary>
/// <remarks>
/// The fetcher extracts assemblies into TFM-bucketed directories under
/// <c>api/lib</c>, and stages reference assemblies under <c>api/refs</c>
/// so the docfx assembly resolver can find them. Designed to be invoked
/// from a Nuke build target as <see cref="FetchPackagesAsync"/>.
/// </remarks>
public sealed partial class NuGetFetcher : INuGetFetcher
{
    /// <summary>
    /// Maximum number of NuGet packages downloaded in parallel. Kept small
    /// to stay friendly to the NuGet feed and avoid rate-limit responses.
    /// </summary>
    private const int MaxParallelDownloads = 3;

    /// <summary>
    /// Number of retry attempts the Polly policy makes per HTTP call before
    /// surfacing the failure.
    /// </summary>
    private const int RetryAttempts = 6;

    /// <summary>
    /// Base URI of the NuGet v3 service index used for endpoint discovery.
    /// </summary>
    private static readonly Uri ServiceIndexUri = new("https://api.nuget.org/v3/index.json");

    /// <summary>
    /// Base URI of the NuGet v3 flat-container endpoint used for version
    /// resolution and <c>.nupkg</c> downloads.
    /// </summary>
    private static readonly Uri FlatContainerUri = new("https://api.nuget.org/v3-flatcontainer/");

    /// <inheritdoc />
    public async Task FetchPackagesAsync(string rootDirectory, string apiPath, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiPath);
        logger ??= NullLogger.Instance;
        var manifestPath = Path.Combine(rootDirectory, "nuget-packages.json");
        var config = PackageConfigReader.Read(manifestPath);

        var libDir = Path.Combine(apiPath, "lib");
        var cacheDir = Path.Combine(apiPath, "cache");
        var refsDir = Path.Combine(apiPath, "refs");

        Directory.CreateDirectory(libDir);
        Directory.CreateDirectory(cacheDir);
        Directory.CreateDirectory(refsDir);

        if (config.ReferencePackages is [_, ..])
        {
            await FetchReferencePackagesAsync(config.ReferencePackages, refsDir, cacheDir, logger, cancellationToken).ConfigureAwait(false);
        }

        var discoveredIds = await DiscoverAllPackagesAsync(config, logger, cancellationToken).ConfigureAwait(false);

        // O(1) dedup of additional packages against the discovered set;
        // the prior LINQ Any() scanned the whole list per additional
        // package which was N*M for no reason.
        var seenIds = new HashSet<string>(discoveredIds.Count, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < discoveredIds.Count; i++)
        {
            seenIds.Add(discoveredIds[i].Id);
        }

        for (var i = 0; i < config.AdditionalPackages.Length; i++)
        {
            var additional = config.AdditionalPackages[i];
            if (seenIds.Add(additional.Id))
            {
                discoveredIds.Add((additional.Id, additional.Version));
            }
        }

        // Exclude lists are user-curated and almost always single-digit
        // sized. A linear scan over a string[] beats HashSet.Contains
        // at that N (no hash compute, better cache locality, no method
        // dispatch through the comparer).
        var excludeIds = config.ExcludePackages;
        var excludePrefixes = config.ExcludePackagePrefixes;
        discoveredIds.RemoveAll(d => IsExcluded(d.Id, excludeIds, excludePrefixes));

        var allPackages = new (string Id, string? Version, string? Tfm)[discoveredIds.Count];
        for (var i = 0; i < discoveredIds.Count; i++)
        {
            var d = discoveredIds[i];
            config.TfmOverrides.TryGetValue(d.Id, out var tfm);
            allPackages[i] = (d.Id, d.Version, tfm);
        }

        LogFetchingPackages(logger, allPackages.Length);
        await FetchGroupAsync(libDir, cacheDir, allPackages, config.TfmPreference, logger, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        // Walk transitive package dependencies via each downloaded
        // .nupkg's nuspec. Splat → Splat.Core/Splat.Logging/Splat.Builder
        // is the canonical case — without these the walker can't follow
        // the type-forwards in the umbrella assembly.
        await ResolveTransitiveDependenciesAsync(
            libDir,
            cacheDir,
            seenIds,
            excludeIds,
            excludePrefixes,
            config.TfmOverrides,
            config.TfmPreference,
            logger,
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        CopyRefsIntoLibDirs(libDir, refsDir, logger);
    }

    /// <summary>
    /// Returns the on-disk path where a <c>.nupkg</c>'s nuspec is
    /// sidecared after extraction (sibling to the nupkg with the
    /// nupkg basename + <c>.nuspec</c>). Used by both the writer in
    /// <see cref="ExtractAssemblies"/> and the reader in
    /// <see cref="ResolveTransitiveDependenciesAsync"/> so the
    /// transitive walk never re-OpenReads the zip.
    /// </summary>
    /// <param name="nupkgPath">Absolute path to the cached <c>.nupkg</c>.</param>
    /// <returns>The sidecar path.</returns>
    internal static string NuspecSidecarPath(string nupkgPath) => nupkgPath + ".nuspec";

    /// <summary>
    /// Returns true when the package id should be skipped per the
    /// user's configured exclude lists.
    /// </summary>
    /// <param name="id">Package identifier to test.</param>
    /// <param name="excludeIds">Exact-match exclude IDs (linear scan; expected single-digit size).</param>
    /// <param name="excludePrefixes">Prefix-match excludes, OrdinalIgnoreCase.</param>
    /// <returns><see langword="true"/> if the package should be skipped; otherwise, <see langword="false"/>.</returns>
    internal static bool IsExcluded(string id, string[] excludeIds, string[] excludePrefixes)
    {
        for (var i = 0; i < excludeIds.Length; i++)
        {
            if (id.Equals(excludeIds[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var idSpan = id.AsSpan();
        for (var i = 0; i < excludePrefixes.Length; i++)
        {
            if (idSpan.StartsWith(excludePrefixes[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true when the transitive walk should skip
    /// <paramref name="id"/> because it's a native / RID-specific
    /// runtime package that contributes zero managed types — checked
    /// in addition to the user's own <c>excludePackages</c> /
    /// <c>excludePackagePrefixes</c>.
    /// </summary>
    /// <param name="id">Discovered transitive package ID.</param>
    /// <returns>True when the package should be skipped on transitive discovery.</returns>
    internal static bool IsDefaultTransitiveSkip(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        ReadOnlySpan<char> s = id.AsSpan();

        if (s.Length == 0)
        {
            return false;
        }

        return (s[0] | 0x20) switch
        {
            'r' => s.StartsWith("runtime.", StringComparison.OrdinalIgnoreCase),
            'm' => IsMicrosoftDefaultTransitiveSkip(s),
            _ => false
        };
    }

    /// <summary>
    /// Determines whether the specified identifier represents a Microsoft default transitive dependency
    /// that should be skipped during processing.
    /// </summary>
    /// <param name="s">The span of characters representing the identifier to evaluate.</param>
    /// <returns>
    /// <c>true</c> if the identifier matches patterns for Microsoft default transitive dependencies
    /// that are to be skipped; otherwise, <c>false</c>.
    /// </returns>
    internal static bool IsMicrosoftDefaultTransitiveSkip(ReadOnlySpan<char> s)
    {
        const string microsoftNet = "Microsoft.NET";

        if (!s.StartsWith(microsoftNet, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        s = s[microsoftNet.Length..];

        if (s.StartsWith(".Native.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        const string core = "Core.";

        if (!s.StartsWith(core, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        s = s[core.Length..];

        return s.StartsWith("Native.", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("UniversalWindowsPlatform", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("Targets", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("Platforms", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("Jit", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("Runtime.", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("Portable.", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true when the file at <paramref name="destPath"/> already
    /// matches <paramref name="entry"/>'s uncompressed length and last
    /// write time — cheap fingerprint that lets the extract loop
    /// skip the I/O on cache-warm runs without hashing the bytes.
    /// </summary>
    /// <param name="destPath">Absolute path to the candidate already-on-disk file.</param>
    /// <param name="entry">Zip entry that would otherwise be extracted to <paramref name="destPath"/>.</param>
    /// <returns>True when the existing file is byte-equivalent to what would be written.</returns>
    internal static bool IsSameAsExtracted(string destPath, ZipArchiveEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destPath);
        ArgumentNullException.ThrowIfNull(entry);

        var info = new FileInfo(destPath);
        if (!info.Exists)
        {
            return false;
        }

        if (info.Length != entry.Length)
        {
            return false;
        }

        // Round to seconds: zip entries store mtime at FAT precision (2s)
        // and ExtractToFile rounds the destination's LastWriteTime to
        // match. Compare in UTC-second resolution to avoid timezone
        // drift and sub-second formatter mismatches.
        var entryStamp = entry.LastWriteTime.UtcDateTime;
        var destStamp = info.LastWriteTimeUtc;
        return Math.Abs((entryStamp - destStamp).TotalSeconds) < 2;
    }

    /// <summary>
    /// Reads each cached <c>.nupkg</c>'s declared dependency IDs and
    /// fetches any not yet pulled. Loops until no new IDs surface (or
    /// a depth cap trips), so a chain like
    /// <c>Splat → Splat.Core → Microsoft.Bcl.AsyncInterfaces</c>
    /// resolves end-to-end without manual <c>nuget-packages.json</c>
    /// edits. Excludes still apply.
    /// </summary>
    /// <param name="libDir">Per-TFM lib directory root.</param>
    /// <param name="cacheDir">Cache directory the FetchGroupAsync downloads land in.</param>
    /// <param name="seenIds">Already-resolved package IDs — mutated as new ones are queued.</param>
    /// <param name="excludeIds">Exact-match exclude IDs (linear scan; expected single-digit size).</param>
    /// <param name="excludePrefixes">Prefix-match excludes.</param>
    /// <param name="tfmOverrides">Per-package TFM overrides applied to newly-resolved packages.</param>
    /// <param name="tfmPreference">TFM preference passed through to FetchGroupAsync.</param>
    /// <param name="logger">Per-iteration progress logger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous closure.</returns>
    private static async Task ResolveTransitiveDependenciesAsync(
        string libDir,
        string cacheDir,
        HashSet<string> seenIds,
        string[] excludeIds,
        string[] excludePrefixes,
        Dictionary<string, string> tfmOverrides,
        string[] tfmPreference,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        const int maxDepth = 8;
        var processedNupkgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var depth = 0; depth < maxDepth; depth++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var newIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var sidecarPaths = Directory.GetFiles(cacheDir, "*.nupkg.nuspec");
            for (var n = 0; n < sidecarPaths.Length; n++)
            {
                var sidecarPath = sidecarPaths[n];
                if (!processedNupkgs.Add(sidecarPath))
                {
                    continue;
                }

                try
                {
                    var deps = await NuspecDependencyReader.ReadDependencyIdsFromFileAsync(sidecarPath, cancellationToken).ConfigureAwait(false);
                    for (var d = 0; d < deps.Length; d++)
                    {
                        var depId = deps[d];
                        if (IsExcluded(depId, excludeIds, excludePrefixes))
                        {
                            continue;
                        }

                        if (IsDefaultTransitiveSkip(depId))
                        {
                            continue;
                        }

                        if (seenIds.Add(depId))
                        {
                            newIds.Add(depId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogNuspecReadFailed(logger, ex, sidecarPath);
                }
            }

            if (newIds.Count == 0)
            {
                return;
            }

            var newIdArray = new string[newIds.Count];
            newIds.CopyTo(newIdArray);
            var newPackages = new (string Id, string? Version, string? Tfm)[newIdArray.Length];
            for (var i = 0; i < newIdArray.Length; i++)
            {
                var id = newIdArray[i];
                tfmOverrides.TryGetValue(id, out var tfm);
                newPackages[i] = (id, null, tfm);
            }

            LogFetchingTransitiveDeps(logger, depth + 1, newPackages.Length);
            await FetchGroupAsync(libDir, cacheDir, newPackages, tfmPreference, logger, cancellationToken).ConfigureAwait(false);
        }

        LogTransitiveDepLimitReached(logger, maxDepth);
    }

    /// <summary>
    /// Constructs the standard exponential-backoff retry policy.
    /// </summary>
    /// <returns>An <see cref="AsyncRetryPolicy"/> configured for HTTP retries.</returns>
    /// <remarks>
    /// Used for every HTTP call in this fetcher. Catches <see cref="HttpRequestException"/> and
    /// waits exponentially longer between attempts (0.2s, 0.4s, 0.8s, ...).
    /// </remarks>
    private static AsyncRetryPolicy CreateRetryPolicy() =>
        Policy.Handle<HttpRequestException>()
            .WaitAndRetryAsync(
                RetryAttempts,
                static attempt => TimeSpan.FromSeconds(0.1 * Math.Pow(2, attempt)));

    /// <summary>
    /// Copies extracted reference assemblies from <c>refs/</c> into each <c>lib/</c> TFM directory.
    /// </summary>
    /// <param name="libDir">Root of the per-TFM lib directories.</param>
    /// <param name="refsDir">Root of the per-TFM refs directories.</param>
    /// <param name="logger">Logger for per-TFM copy summaries.</param>
    /// <remarks>
    /// Uses <see cref="TfmResolver.FindBestRefsTfm"/> to pair them. Files that
    /// already exist in the destination are not overwritten so package
    /// assemblies always win over reference shims of the same name.
    /// </remarks>
    private static void CopyRefsIntoLibDirs(string libDir, string refsDir, ILogger logger)
    {
        if (!Directory.Exists(libDir) || !Directory.Exists(refsDir))
        {
            return;
        }

        List<string> refsTfms = [];
        foreach (var dir in Directory.EnumerateDirectories(refsDir))
        {
            refsTfms.Add(Path.GetFileName(dir.AsSpan()).ToString());
        }

        foreach (var libTfmDir in Directory.EnumerateDirectories(libDir))
        {
            var libTfm = Path.GetFileName(libTfmDir.AsSpan()).ToString();
            if (TfmResolver.FindBestRefsTfm(libTfm, refsTfms) is not { } bestRef)
            {
                continue;
            }

            var refDir = Path.Combine(refsDir, bestRef);
            var count = 0;
            foreach (var refDll in Directory.EnumerateFiles(refDir, "*.dll"))
            {
                var destPath = Path.Combine(libTfmDir, Path.GetFileName(refDll.AsSpan()).ToString());
                if (File.Exists(destPath))
                {
                    continue;
                }

                File.Copy(refDll, destPath);
                count++;
            }

            if (count > 0)
            {
                LogCopiedRefs(logger, count, libTfm, bestRef);
            }
        }
    }

    /// <summary>
    /// Discovers every package owned by any of the configured NuGet owner accounts.
    /// </summary>
    /// <param name="config">The parsed package configuration.</param>
    /// <param name="logger">Logger for endpoint discovery and per-owner counts.</param>
    /// <param name="cancellationToken">Cancellation token honoured between owner queries and per HTTP call.</param>
    /// <returns>A list of (id, version) tuples; versions are <see langword="null"/> at this stage and resolved later.</returns>
    /// <remarks>
    /// Consults the NuGet search service for each owner defined in the configuration.
    /// </remarks>
    private static async Task<List<(string Id, string? Version)>> DiscoverAllPackagesAsync(PackageConfig config, ILogger logger, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        var retryPolicy = CreateRetryPolicy();

        var searchEndpoint = await ResolveSearchEndpointAsync(client, retryPolicy, cancellationToken).ConfigureAwait(false);
        LogUsingSearchEndpoint(logger, searchEndpoint);

        List<(string Id, string? Version)> allIds = [];
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < config.NugetPackageOwners.Length; i++)
        {
            var owner = config.NugetPackageOwners[i];
            cancellationToken.ThrowIfCancellationRequested();
            var ids = await DiscoverPackagesByOwnerAsync(client, retryPolicy, searchEndpoint, owner, cancellationToken).ConfigureAwait(false);
            LogDiscoveredOwnerPackages(logger, ids.Count, owner);
            for (var j = 0; j < ids.Count; j++)
            {
                var id = ids[j];
                if (seenIds.Add(id))
                {
                    allIds.Add((id, null));
                }
            }
        }

        return allIds;
    }

    /// <summary>
    /// Fetches reference-only packages and extracts their reference assemblies.
    /// </summary>
    /// <param name="packages">Reference packages to fetch.</param>
    /// <param name="refsDir">Output root for extracted reference assemblies.</param>
    /// <param name="cacheDir">Cache directory for the downloaded <c>.nupkg</c> files.</param>
    /// <param name="logger">Logger for per-package progress and failure messages.</param>
    /// <param name="cancellationToken">Cancellation token honoured between packages and per HTTP call.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// <remarks>
    /// Extracts reference assemblies into per-TFM directories under <c>refs/</c>.
    /// Failures on individual packages are logged and skipped so one bad package does
    /// not abort the whole run.
    /// </remarks>
    private static async Task FetchReferencePackagesAsync(
        ReferencePackage[] packages,
        string refsDir,
        string cacheDir,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        var retryPolicy = CreateRetryPolicy();

        for (var i = 0; i < packages.Length; i++)
        {
            var pkg = packages[i];
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var idLower = pkg.Id.ToLowerInvariant();

                var version = pkg.Version;
                if (version == null)
                {
                    LogResolvingRefVersion(logger, pkg.Id);
                    version = await ResolveLatestStableVersionAsync(client, retryPolicy, idLower, cancellationToken).ConfigureAwait(false);
                    if (version == null)
                    {
                        LogRefVersionUnresolved(logger, pkg.Id);
                        continue;
                    }
                }

                LogUsingRefPackage(logger, pkg.Id, version);

                var versionLower = version.ToLowerInvariant();
                var nupkgPath = Path.Combine(cacheDir, $"{idLower}.{versionLower}.nupkg");
                if (!File.Exists(nupkgPath))
                {
                    LogDownloadingPackage(logger, pkg.Id, version);
                    await DownloadNupkgAsync(client, retryPolicy, idLower, versionLower, nupkgPath, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    LogUsingCachedPackage(logger, pkg.Id, version);
                }

                var tfmRefsDir = Path.Combine(refsDir, pkg.TargetTfm);
                Directory.CreateDirectory(tfmRefsDir);
                ExtractReferenceAssemblies(nupkgPath, tfmRefsDir, pkg.Id, pkg.PathPrefix, logger);
            }
            catch (Exception ex)
            {
                LogRefPackageFetchFailed(logger, ex, pkg.Id);
            }
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when the supplied stream contains a pure managed (IL-only) assembly.
    /// </summary>
    /// <param name="stream">Stream positioned at the start of a candidate PE file.</param>
    /// <returns><see langword="true"/> if the assembly is managed and IL-only; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// Filters out native and mixed-mode DLLs that Roslyn cannot consume as references
    /// (for example <c>System.EnterpriseServices.Wrapper.dll</c>).
    /// </remarks>
    private static bool IsManagedAssembly(Stream stream)
    {
        try
        {
            using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            return peReader is { HasMetadata: true, PEHeaders.CorHeader: not null }
                   && peReader.PEHeaders.CorHeader.Flags.HasFlag(CorFlags.ILOnly);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Extracts managed <c>.dll</c> files from a package.
    /// </summary>
    /// <param name="nupkgPath">Path to the <c>.nupkg</c> on disk.</param>
    /// <param name="tfmRefsDir">Destination directory for the extracted assemblies.</param>
    /// <param name="packageId">Package identifier, for logging only.</param>
    /// <param name="pathPrefix">Path prefix inside the archive to extract from (typically <c>ref/</c>).</param>
    /// <param name="logger">Logger for per-entry skip notices and the extraction summary.</param>
    /// <remarks>
    /// Extracts every managed <c>.dll</c> under the requested path prefix.
    /// Native and mixed-mode DLLs are filtered out using <see cref="IsManagedAssembly"/>.
    /// </remarks>
    private static void ExtractReferenceAssemblies(
        string nupkgPath,
        string tfmRefsDir,
        string packageId,
        string pathPrefix,
        ILogger logger)
    {
        using var archive = ZipFile.OpenRead(nupkgPath);
        var prefix = pathPrefix.TrimEnd('/') + "/";
        var count = 0;

        for (var i = 0; i < archive.Entries.Count; i++)
        {
            var entry = archive.Entries[i];
            if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
            if (ext is not ".dll")
            {
                continue;
            }

            using var entryStream = entry.Open();

            // Pre-size to the entry's known uncompressed length so the
            // backing byte[] is allocated once at the correct size; the
            // default MemoryStream doubles its buffer on every Write.
            // Two-file-op alternative (write + reopen for PE check) was
            // measured at +7.6% wall time for a -6% allocation win, so
            // the buffered round-trip stays.
            using var memStream = new MemoryStream(checked((int)entry.Length));
            entryStream.CopyTo(memStream);

            memStream.Position = 0;
            if (!IsManagedAssembly(memStream))
            {
                LogSkippingNativeDll(logger, entry.Name);
                continue;
            }

            memStream.Position = 0;
            var destPath = Path.Combine(tfmRefsDir, entry.Name);
            using var fileStream = new FileStream(destPath, FileMode.Create);
            memStream.CopyTo(fileStream);
            count++;
        }

        LogExtractedRefs(logger, count, packageId, pathPrefix);
    }

    /// <summary>
    /// Reads the NuGet v3 service index and returns the URI of the search service.
    /// </summary>
    /// <param name="client">HTTP client used for the request.</param>
    /// <param name="retryPolicy">Retry policy applied to the request.</param>
    /// <param name="cancellationToken">Cancellation token honoured by the HTTP request.</param>
    /// <returns>The resolved search endpoint URI.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the service index does not advertise the required search service type.</exception>
    private static async Task<Uri> ResolveSearchEndpointAsync(
        HttpClient client,
        AsyncRetryPolicy retryPolicy,
        CancellationToken cancellationToken)
    {
        Uri? endpoint = null;

        await retryPolicy.ExecuteAsync(
            async ct =>
            {
                var response = await client.GetAsync(ServiceIndexUri, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

                foreach (var resource in doc.RootElement.GetProperty("resources"u8).EnumerateArray())
                {
                    if (!resource.GetProperty("@type"u8).ValueEquals("SearchQueryService/3.5.0"u8))
                    {
                        continue;
                    }

                    if (resource.TryGetProperty("@id"u8, out var idEl) && idEl.GetString() is { } id)
                    {
                        endpoint = new(id);
                    }

                    break;
                }
            },
            cancellationToken).ConfigureAwait(false);

        return endpoint ?? throw new InvalidOperationException(
            $"Could not find {Encoding.UTF8.GetString("SearchQueryService/3.5.0"u8)} in NuGet service index");
    }

    /// <summary>
    /// Pages through the NuGet search service to enumerate every package owned by the supplied owner.
    /// </summary>
    /// <param name="client">HTTP client used for the requests.</param>
    /// <param name="retryPolicy">Retry policy applied to each request.</param>
    /// <param name="searchEndpoint">The resolved search service endpoint.</param>
    /// <param name="owner">The NuGet owner account to query.</param>
    /// <param name="cancellationToken">Cancellation token honoured between pages and per HTTP call.</param>
    /// <returns>A list of discovered package identifiers.</returns>
    /// <remarks>
    /// Packages (including unlisted) are enumerated. Deprecated packages are skipped
    /// so they don't appear in the documentation. Deduplication is handled by the caller.
    /// </remarks>
    private static async Task<List<string>> DiscoverPackagesByOwnerAsync(
        HttpClient client,
        AsyncRetryPolicy retryPolicy,
        Uri searchEndpoint,
        string owner,
        CancellationToken cancellationToken)
    {
        List<string> packageIds = [];
        var skip = 0;
        const int take = 100;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var queryBuilder = new UriBuilder(searchEndpoint)
            {
                Query = $"q=owner:{Uri.EscapeDataString(owner)}&take={take}&skip={skip}&semVerLevel=2.0.0",
            };
            var url = queryBuilder.Uri;
            var totalHits = 0;

            await retryPolicy.ExecuteAsync(
                async ct =>
                {
                    var response = await client.GetAsync(url, ct).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();

                    await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

                    totalHits = doc.RootElement.GetProperty("totalHits"u8).GetInt32();

                    foreach (var result in doc.RootElement.GetProperty("data"u8).EnumerateArray())
                    {
                        // Skip deprecated packages (still listed but author-flagged).
                        if (result.TryGetProperty("deprecation"u8, out _))
                        {
                            continue;
                        }

                        // Skip packages with known vulnerabilities so the docs site
                        // never advertises a version a consumer should not pull.
                        if (result.TryGetProperty("vulnerabilities"u8, out var vulns)
                            && vulns.ValueKind == JsonValueKind.Array
                            && vulns.GetArrayLength() > 0)
                        {
                            continue;
                        }

                        var id = result.GetProperty("id"u8).GetString();
                        if (id != null)
                        {
                            packageIds.Add(id);
                        }
                    }
                },
                cancellationToken).ConfigureAwait(false);

            skip += take;
            if (skip >= totalHits)
            {
                break;
            }
        }

        return packageIds;
    }

    /// <summary>
    /// Downloads and extracts every package in <paramref name="packages"/> in parallel.
    /// </summary>
    /// <param name="libDir">Root of the per-TFM lib output directories.</param>
    /// <param name="cacheDir">Cache directory for downloaded <c>.nupkg</c> files.</param>
    /// <param name="packages">Tuples of package identifier, optional pinned version, and optional TFM override.</param>
    /// <param name="tfmPreference">The global TFM preference list.</param>
    /// <param name="logger">Logger for per-package progress and failure messages.</param>
    /// <param name="cancellationToken">Cancellation token forwarded to <see cref="ParallelOptions.CancellationToken"/> and the per-package HTTP calls.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// <remarks>
    /// Downloads are limited to <see cref="MaxParallelDownloads"/>. Cached <c>.nupkg</c> files
    /// are reused when they already exist on disk. Each package's failures are isolated so
    /// a single bad package does not abort the whole run.
    /// </remarks>
    private static async Task FetchGroupAsync(
        string libDir,
        string cacheDir,
        (string Id, string? Version, string? Tfm)[] packages,
        string[] tfmPreference,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        var retryPolicy = CreateRetryPolicy();

        // Parallel.ForEachAsync replaces the prior SemaphoreSlim + Select +
        // Task.WhenAll dance. Same concurrency budget, no IDisposable to
        // manage, no analyser flow-analysis ceremony.
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxParallelDownloads,
            CancellationToken = cancellationToken,
        };
        var states = new FetchState[packages.Length];
        for (var i = 0; i < packages.Length; i++)
        {
            states[i] = new(packages[i], client, retryPolicy, libDir, cacheDir, tfmPreference, logger);
        }

        await Parallel.ForEachAsync(
            states,
            parallelOptions,
            static async (state, ct) =>
            {
                var pkg = state.Package;
                try
                {
                    var idLower = pkg.Id.ToLowerInvariant();

                    var version = pkg.Version;
                    if (version is null)
                    {
                        LogResolvingVersion(state.Logger, pkg.Id);
                        version = await ResolveLatestStableVersionAsync(state.Client, state.RetryPolicy, idLower, ct).ConfigureAwait(false);
                        if (version is null)
                        {
                            LogVersionUnresolved(state.Logger, pkg.Id);
                            return;
                        }
                    }

                    LogUsingPackage(state.Logger, pkg.Id, version);

                    var versionLower = version.ToLowerInvariant();
                    var nupkgPath = Path.Combine(state.CacheDir, $"{idLower}.{versionLower}.nupkg");
                    var nuspecSidecarPath = NuspecSidecarPath(nupkgPath);

                    if (File.Exists(nuspecSidecarPath))
                    {
                        LogUsingCachedPackage(state.Logger, pkg.Id, version);
                        return;
                    }

                    var lockPath = PackageInstallLock.GetLockFilePath(state.CacheDir, nupkgPath);
                    await PackageInstallLock.RunUnderLockAsync(
                        lockPath,
                        alreadyDone: () => File.Exists(nuspecSidecarPath),
                        work: async lockCt =>
                        {
                            if (!File.Exists(nupkgPath))
                            {
                                LogDownloadingPackage(state.Logger, pkg.Id, version);
                                await DownloadNupkgAsync(state.Client, state.RetryPolicy, idLower, versionLower, nupkgPath, lockCt).ConfigureAwait(false);
                            }

                            ExtractAssemblies(nupkgPath, state.LibDir, pkg.Id, pkg.Tfm, state.TfmPreference, state.Logger);
                            LogExtractedPackage(state.Logger, pkg.Id, version);

                            try
                            {
                                File.Delete(nupkgPath);
                            }
                            catch
                            {
                                // Best-effort.
                            }
                        },
                        ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogPackageProcessFailed(state.Logger, ex, pkg.Id);
                }
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the latest stable (non-prerelease) version of the named package.
    /// </summary>
    /// <param name="client">HTTP client used for the request.</param>
    /// <param name="retryPolicy">Retry policy applied to the request.</param>
    /// <param name="idLower">Lowercased package identifier.</param>
    /// <param name="cancellationToken">Cancellation token honoured by the HTTP request.</param>
    /// <returns>The resolved version string, or <see langword="null"/> when the package has no stable releases.</returns>
    /// <remarks>
    /// Fetches the version index from the NuGet flat-container endpoint.
    /// </remarks>
    private static async Task<string?> ResolveLatestStableVersionAsync(
        HttpClient client,
        AsyncRetryPolicy retryPolicy,
        string idLower,
        CancellationToken cancellationToken)
    {
        string? result = null;
        var url = new Uri(FlatContainerUri, $"{idLower}/index.json");

        await retryPolicy.ExecuteAsync(
            async ct =>
            {
                var response = await client.GetAsync(url, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

                // Single pass: parse each candidate via NuGetVersion so we honour
                // SemVer ordering (e.g. 10.0.0 > 9.0.0) and properly skip
                // prereleases / build metadata, rather than relying on the API's
                // ascending-string order.
                NuGetVersion? best = null;
                foreach (var versionElement in doc.RootElement.GetProperty("versions"u8).EnumerateArray())
                {
                    if (versionElement.GetString() is not { } raw
                        || !NuGetVersion.TryParse(raw, out var parsed)
                        || parsed.IsPrerelease)
                    {
                        continue;
                    }

                    if (best is null || parsed > best)
                    {
                        best = parsed;
                    }
                }

                result = best?.ToNormalizedString();
            },
            cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Downloads a specific package version's <c>.nupkg</c> to the supplied path on disk.
    /// </summary>
    /// <param name="client">HTTP client used for the request.</param>
    /// <param name="retryPolicy">Retry policy applied to the request.</param>
    /// <param name="idLower">Lowercased package identifier.</param>
    /// <param name="versionLower">Lowercased package version.</param>
    /// <param name="outputPath">Destination file path.</param>
    /// <param name="cancellationToken">Cancellation token honoured by the HTTP request and the streaming copy.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    private static async Task DownloadNupkgAsync(
        HttpClient client,
        AsyncRetryPolicy retryPolicy,
        string idLower,
        string versionLower,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var url = new Uri(FlatContainerUri, $"{idLower}/{versionLower}/{idLower}.{versionLower}.nupkg");

        await retryPolicy.ExecuteAsync(
            async ct =>
            {
                var response = await client.GetAsync(url, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var fileStream = new FileStream(outputPath, FileMode.Create);
                await contentStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Extracts assemblies from a <c>.nupkg</c> into TFM-bucketed directories.
    /// </summary>
    /// <param name="nupkgPath">Path to the <c>.nupkg</c> on disk.</param>
    /// <param name="libDir">Root of the per-TFM lib output directories.</param>
    /// <param name="packageId">Package identifier, for logging.</param>
    /// <param name="tfmOverride">Optional per-package TFM override.</param>
    /// <param name="tfmPreference">The global TFM preference list.</param>
    /// <param name="logger">Logger for skip/extract notices and TFM selection summary.</param>
    /// <remarks>
    /// Selects the best TFM in the supplied <c>.nupkg</c>'s <c>lib/</c>
    /// folder via <see cref="TfmResolver.SelectAllSupportedTfms"/>,
    /// then extracts every supported variant's <c>.dll</c> and
    /// <c>.xml</c> files into <c>libDir/&lt;tfm&gt;/</c>. Extracting
    /// every supported TFM (rather than just one canonical pick) is
    /// what enables the downstream merger to surface a real
    /// "Applies to" listing in the docs — readers can see exactly
    /// which TFMs ship the type instead of getting whichever variant
    /// the fetcher happened to prefer.
    /// </remarks>
    [SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging", Justification = "False positive, in a helper to not invoke unless necessary")]
    private static void ExtractAssemblies(
        string nupkgPath,
        string libDir,
        string packageId,
        string? tfmOverride,
        string[] tfmPreference,
        ILogger logger)
    {
        using var archive = ZipFile.OpenRead(nupkgPath);

        // Single foreach replaces the prior Where + GroupBy + Where +
        // ToDictionary chain. Allocates one Dictionary and one List per
        // distinct TFM rather than the LINQ pipeline's grouping internals.
        var libEntries = new Dictionary<string, List<ZipArchiveEntry>>(StringComparer.OrdinalIgnoreCase);
        ZipArchiveEntry? nuspecEntry = null;
        for (var i = 0; i < archive.Entries.Count; i++)
        {
            var entry = archive.Entries[i];
            if (entry.Name is null or [])
            {
                continue;
            }

            // Capture the nuspec while we already have the central
            // directory open. Sidecar-extracting it here means the
            // transitive-dep walk never has to re-OpenRead the zip
            // just to read deps — it tail-reads the XML from disk.
            if (nuspecEntry is null && NuspecDependencyReader.IsRootNuspecEntry(entry.FullName))
            {
                nuspecEntry = entry;
            }

            var path = entry.FullName.AsSpan();
            if (!path.StartsWith("lib/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Pull the TFM segment from "lib/<tfm>/..." without
            // allocating an intermediate string[] from Split.
            var afterLib = path["lib/".Length..];
            var nextSlash = afterLib.IndexOf('/');
            if (nextSlash <= 0)
            {
                continue;
            }

            var tfm = afterLib[..nextSlash].ToString();
            if (!libEntries.TryGetValue(tfm, out var list))
            {
                list = [];
                libEntries[tfm] = list;
            }

            list.Add(entry);
        }

        if (libEntries.Count == 0)
        {
            LogNoLibEntries(logger, packageId);
            return;
        }

        var availableTfms = new List<string>(libEntries.Keys);
        var selectedTfms = TfmResolver.SelectAllSupportedTfms(availableTfms, tfmOverride, tfmPreference);
        if (selectedTfms.Count == 0)
        {
            LogInvokerHelper.Invoke(
                logger,
                LogLevel.Warning,
                packageId,
                availableTfms,
                static (l, id, tfms) =>
                {
                    if (!l.IsEnabled(LogLevel.Warning))
                    {
                        return;
                    }

                    var available = string.Join(", ", tfms);
                    LogNoSupportedTfm(l, id, available);
                });
            return;
        }

        LogInvokerHelper.Invoke(
            logger,
            LogLevel.Information,
            packageId,
            selectedTfms,
            static (l, id, tfms) =>
            {
                if (!l.IsEnabled(LogLevel.Information))
                {
                    return;
                }

                var selected = string.Join(", ", tfms);
                LogExtractingTfms(l, id, tfms.Count, selected);
            });

        for (var i = 0; i < selectedTfms.Count; i++)
        {
            var selectedTfm = selectedTfms[i];
            var tfmLibDir = Path.Combine(libDir, selectedTfm);
            Directory.CreateDirectory(tfmLibDir);

            var entries = libEntries[selectedTfm];
            for (var j = 0; j < entries.Count; j++)
            {
                var entry = entries[j];
                var ext = Path.GetExtension(entry.Name.AsSpan());
                if (!ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var destPath = Path.Combine(tfmLibDir, entry.Name);

                // Skip-if-identical: the transitive-dep closure can
                // run up to maxDepth rounds, each one re-globbing the
                // cache and re-entering ExtractAssemblies. Without this
                // check every package's per-TFM DLL set re-extracts
                // every round (and SelectAllSupportedTfms multiplies
                // that by every compatible TFM family) — on a heavy
                // fixture that fans out to tens of GB of redundant
                // disk writes. Compare uncompressed length + last
                // write time so a real package upgrade still copies.
                if (IsSameAsExtracted(destPath, entry))
                {
                    continue;
                }

                entry.ExtractToFile(destPath, overwrite: true);
            }
        }

        // Sidecar the nuspec next to the .nupkg so the transitive-dep
        // walk reads cheap XML from disk instead of re-OpenRead-ing
        // the zip. Skip-if-same matches the DLL extraction policy.
        if (nuspecEntry is null)
        {
            return;
        }

        var nuspecSidecarPath = NuspecSidecarPath(nupkgPath);
        if (IsSameAsExtracted(nuspecSidecarPath, nuspecEntry))
        {
            return;
        }

        nuspecEntry.ExtractToFile(nuspecSidecarPath, overwrite: true);
    }

    /// <summary>Logs the start of the parallel fetch over discovered packages.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="packageCount">Total packages queued for download.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Fetching {PackageCount} packages...")]
    private static partial void LogFetchingPackages(ILogger logger, int packageCount);

    /// <summary>Logs each transitive-dependency fetch round.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="round">1-based round number (depth into the dep graph).</param>
    /// <param name="count">Number of newly-discovered packages queued in this round.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Fetching transitive dep round {Round}: {Count} new package(s)")]
    private static partial void LogFetchingTransitiveDeps(ILogger logger, int round, int count);

    /// <summary>Logs a per-nupkg nuspec read failure — non-fatal, the closure loop skips it.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="ex">Exception thrown while reading the nuspec.</param>
    /// <param name="nupkgPath">Path to the package whose nuspec we couldn't read.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read nuspec dependencies from {NupkgPath}; skipping its transitive deps")]
    private static partial void LogNuspecReadFailed(ILogger logger, Exception ex, string nupkgPath);

    /// <summary>Logs that we hit the depth cap before reaching a fixed point.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="maxDepth">Cap value that was hit.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Transitive dep walk reached depth cap ({MaxDepth}); some deps may not have been resolved")]
    private static partial void LogTransitiveDepLimitReached(ILogger logger, int maxDepth);

    /// <summary>Logs reference assemblies copied from a refs/ TFM into a lib/ TFM directory.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="count">Files copied.</param>
    /// <param name="libTfm">Destination lib/ TFM.</param>
    /// <param name="refsTfm">Source refs/ TFM.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Copied {Count} reference assemblies into lib/{LibTfm} (from refs/{RefsTfm})")]
    private static partial void LogCopiedRefs(ILogger logger, int count, string libTfm, string refsTfm);

    /// <summary>Logs the NuGet search endpoint discovered from the service index.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="endpoint">Resolved search endpoint URI.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Using NuGet search endpoint: {Endpoint}")]
    private static partial void LogUsingSearchEndpoint(ILogger logger, Uri endpoint);

    /// <summary>Logs the count of packages discovered for a single owner.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="count">Packages owned by the account.</param>
    /// <param name="owner">NuGet owner account.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Discovered {Count} packages for owner '{Owner}'")]
    private static partial void LogDiscoveredOwnerPackages(ILogger logger, int count, string owner);

    /// <summary>Logs that we are resolving the latest version of a reference package (no pin in config).</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="id">Reference package identifier.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Resolving latest version for reference package {Id}...")]
    private static partial void LogResolvingRefVersion(ILogger logger, string id);

    /// <summary>Logs that we could not resolve a version for a reference package and are skipping it.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="id">Reference package identifier.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Could not resolve version for reference package {Id}, skipping")]
    private static partial void LogRefVersionUnresolved(ILogger logger, string id);

    /// <summary>Logs the version being used for a reference package.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="id">Reference package identifier.</param>
    /// <param name="version">Resolved package version.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Using reference package {Id} v{Version}")]
    private static partial void LogUsingRefPackage(ILogger logger, string id, string version);

    /// <summary>Logs failure to fetch a reference package (run continues).</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="exception">Underlying fetch exception.</param>
    /// <param name="id">Reference package identifier.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to fetch reference package {Id}")]
    private static partial void LogRefPackageFetchFailed(ILogger logger, Exception exception, string id);

    /// <summary>Logs that a native (non-managed) DLL inside a reference package was skipped.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="entryName">Filename of the skipped entry.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "  Skipping native DLL: {EntryName}")]
    private static partial void LogSkippingNativeDll(ILogger logger, string entryName);

    /// <summary>Logs the count of reference assemblies extracted for a package.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="count">Assemblies extracted.</param>
    /// <param name="packageId">Reference package identifier.</param>
    /// <param name="pathPrefix">Archive path prefix the assemblies came from (typically <c>ref/</c>).</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Extracted {Count} reference assemblies from {PackageId} ({PathPrefix})")]
    private static partial void LogExtractedRefs(ILogger logger, int count, string packageId, string pathPrefix);

    /// <summary>Logs that we are resolving the latest version of a regular package (no pin in config).</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="id">Package identifier.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Resolving latest version for {Id}...")]
    private static partial void LogResolvingVersion(ILogger logger, string id);

    /// <summary>Logs that we could not resolve a version for a package and are skipping it.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="id">Package identifier.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Could not resolve version for {Id}")]
    private static partial void LogVersionUnresolved(ILogger logger, string id);

    /// <summary>Logs the version being used for a regular package.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="id">Package identifier.</param>
    /// <param name="version">Resolved package version.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Using {Id} v{Version}")]
    private static partial void LogUsingPackage(ILogger logger, string id, string version);

    /// <summary>Logs that a package <c>.nupkg</c> is being downloaded.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="id">Package identifier.</param>
    /// <param name="version">Package version being downloaded.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Downloading {Id} v{Version}...")]
    private static partial void LogDownloadingPackage(ILogger logger, string id, string version);

    /// <summary>Logs that a previously cached <c>.nupkg</c> was reused.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="id">Package identifier.</param>
    /// <param name="version">Package version.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Using cached {Id} v{Version}")]
    private static partial void LogUsingCachedPackage(ILogger logger, string id, string version);

    /// <summary>Logs successful extraction of a package's assemblies.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="id">Package identifier.</param>
    /// <param name="version">Package version.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Extracted {Id} v{Version}")]
    private static partial void LogExtractedPackage(ILogger logger, string id, string version);

    /// <summary>Logs failure to process a package end-to-end (run continues).</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="exception">Underlying processing exception.</param>
    /// <param name="id">Package identifier.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process {Id}")]
    private static partial void LogPackageProcessFailed(ILogger logger, Exception exception, string id);

    /// <summary>Logs that the package contained no <c>lib/</c> entries (source-gen or meta-package).</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="packageId">Package identifier.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "No lib/ entries found in {PackageId}, skipping (source generator or meta-package)")]
    private static partial void LogNoLibEntries(ILogger logger, string packageId);

    /// <summary>Logs that none of the package's available TFMs match our preferences (skipped).</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="packageId">Package identifier.</param>
    /// <param name="available">Comma-separated list of TFMs the package shipped.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "  {PackageId}: no supported TFM in package, skipping. Available: {Available}")]
    private static partial void LogNoSupportedTfm(ILogger logger, string packageId, string available);

    /// <summary>Logs which TFM variants the fetcher selected for extraction.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="packageId">Package identifier.</param>
    /// <param name="tfmCount">Number of TFMs selected.</param>
    /// <param name="selectedTfms">Comma-separated list of selected TFMs.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "  {PackageId}: extracting {TfmCount} TFM(s) — {SelectedTfms}")]
    private static partial void LogExtractingTfms(ILogger logger, string packageId, int tfmCount, string selectedTfms);

    /// <summary>
    /// Encapsulates the state required for a single package fetch operation
    /// inside a parallel loop, avoiding closure captures.
    /// </summary>
    /// <param name="Package">The package details (ID, version, and optional TFM override).</param>
    /// <param name="Client">The shared HTTP client for downloads.</param>
    /// <param name="RetryPolicy">The resilience policy for transient HTTP failures.</param>
    /// <param name="LibDir">The root directory where assemblies are extracted.</param>
    /// <param name="CacheDir">The directory where downloaded <c>.nupkg</c> files are stored.</param>
    /// <param name="TfmPreference">The global TFM preference order.</param>
    /// <param name="Logger">The logger for operation progress and errors.</param>
    private sealed record FetchState(
        (string Id, string? Version, string? Tfm) Package,
        HttpClient Client,
        AsyncRetryPolicy RetryPolicy,
        string LibDir,
        string CacheDir,
        string[] TfmPreference,
        ILogger Logger);
}
