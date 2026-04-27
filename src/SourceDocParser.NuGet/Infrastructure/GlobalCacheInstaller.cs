// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.Extensions.Logging.Abstractions;
using SourceDocParser.NuGet.Models;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>
/// Installs a NuGet package into the SDK-shared global cache
/// (<c>~/.nuget/packages/&lt;id&gt;/&lt;ver&gt;/</c>) — composes the
/// nuget.config helpers (sources / disabled / credentials /
/// fallback folders) so the install honours the same precedence
/// chain <c>dotnet restore</c> uses, then short-circuits via the
/// <c>.nupkg.metadata</c> marker so re-runs and concurrent
/// installs don't repeat work.
/// </summary>
public sealed class GlobalCacheInstaller : IDisposable
{
    /// <summary>Project root used to start the nuget.config discovery walk.</summary>
    private readonly string _workingFolder;

    /// <summary>Logger threaded through to the discovery / lock helpers.</summary>
    private readonly ILogger _logger;

    /// <summary>HTTP surface used for service-index reads and .nupkg downloads.</summary>
    private readonly INuGetFeedHttpClient _feedHttp;

    /// <summary>True when this instance created <see cref="_feedHttp"/> and is responsible for disposing it.</summary>
    private readonly bool _ownsFeedHttp;

    /// <summary>Per-source flat-container endpoint cache (one HTTP roundtrip per feed).</summary>
    private readonly Dictionary<string, string?> _flatContainerByFeed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolved global packages folder; set by InitializeAsync.</summary>
    private string? _globalPackagesFolder;

    /// <summary>Resolved enabled sources (after disabled-set filter); set by InitializeAsync.</summary>
    private PackageSource[]? _enabledSources;

    /// <summary>Per-source credentials; set by InitializeAsync.</summary>
    private Dictionary<string, PackageSourceCredential>? _credentials;

    /// <summary>Resolved fallback folders probed before any HTTP; set by InitializeAsync.</summary>
    private string[]? _fallbackFolders;

    /// <summary>Initializes a new instance of the <see cref="GlobalCacheInstaller"/> class.</summary>
    /// <param name="workingFolder">Project root used to start the nuget.config discovery walk.</param>
    /// <param name="logger">Optional logger; defaults to a no-op.</param>
    /// <param name="http">Optional HTTP client; pass-in lets tests share a handler. Wrapped in a default <see cref="NuGetFeedHttpClient"/>.</param>
    public GlobalCacheInstaller(string workingFolder, ILogger? logger = null, HttpClient? http = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingFolder);
        _workingFolder = workingFolder;
        _logger = logger ?? NullLogger.Instance;
        var ownsHttp = http is null;
        _feedHttp = new NuGetFeedHttpClient(http ?? new HttpClient(), ownsHttp: ownsHttp);
        _ownsFeedHttp = true;
    }

    /// <summary>Initializes a new instance of the <see cref="GlobalCacheInstaller"/> class with an injectable feed client.</summary>
    /// <param name="workingFolder">Project root used to start the nuget.config discovery walk.</param>
    /// <param name="logger">Optional logger; defaults to a no-op.</param>
    /// <param name="feedHttp">Feed HTTP surface; tests pass a fake to drive every status-code branch.</param>
    internal GlobalCacheInstaller(string workingFolder, ILogger? logger, INuGetFeedHttpClient feedHttp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingFolder);
        ArgumentNullException.ThrowIfNull(feedHttp);
        _workingFolder = workingFolder;
        _logger = logger ?? NullLogger.Instance;
        _feedHttp = feedHttp;
        _ownsFeedHttp = false;
    }

    /// <summary>Gets the resolved global packages folder.</summary>
    /// <exception cref="InvalidOperationException">When called before <see cref="InitializeAsync"/>.</exception>
    public string GlobalPackagesFolder =>
        _globalPackagesFolder ?? throw new InvalidOperationException("Call InitializeAsync first.");

    /// <summary>Gets the resolved enabled package sources, ordered by precedence.</summary>
    public IReadOnlyList<PackageSource> EnabledSources =>
        _enabledSources ?? throw new InvalidOperationException("Call InitializeAsync first.");

    /// <summary>Resolves the global cache, sources, credentials, disabled set, and fallback folders.</summary>
    /// <param name="cancellationToken">Token observed across each parse.</param>
    /// <returns>A task representing the asynchronous initialise step.</returns>
    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        _globalPackagesFolder = await NuGetConfigDiscovery.ResolveAsync(_workingFolder, cancellationToken).ConfigureAwait(false);
        var allSources = await NuGetConfigDiscovery.ResolvePackageSourcesAsync(_workingFolder, cancellationToken).ConfigureAwait(false);
        var disabled = await NuGetConfigDiscovery.ResolveDisabledSourcesAsync(_workingFolder, cancellationToken).ConfigureAwait(false);

        var enabled = new List<PackageSource>(allSources.Length);
        for (var i = 0; i < allSources.Length; i++)
        {
            if (!disabled.Contains(allSources[i].Key))
            {
                enabled.Add(allSources[i]);
            }
        }

        _enabledSources = [.. enabled];
        _credentials = await NuGetConfigDiscovery.ResolveCredentialsAsync(_workingFolder, cancellationToken).ConfigureAwait(false);
        _fallbackFolders = await NuGetConfigDiscovery.ResolveFallbackFoldersAsync(_workingFolder, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the install path for <paramref name="packageId"/> /
    /// <paramref name="packageVersion"/> after ensuring the package is
    /// available — short-circuits when already in the global cache or a
    /// fallback folder, downloads + extracts otherwise.
    /// </summary>
    /// <param name="packageId">NuGet package id.</param>
    /// <param name="packageVersion">Normalised version string.</param>
    /// <param name="cancellationToken">Token observed across the install.</param>
    /// <returns>The absolute install directory the caller can enumerate <c>lib/&lt;tfm&gt;/</c> under.</returns>
    public async ValueTask<string> InstallAsync(string packageId, string packageVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
        if (_globalPackagesFolder is null || _enabledSources is null || _fallbackFolders is null || _credentials is null)
        {
            throw new InvalidOperationException("Call InitializeAsync first.");
        }

        var installPath = NuGetGlobalCache.GetPackageInstallPath(_globalPackagesFolder, packageId, packageVersion);
        if (NuGetGlobalCache.IsPackageInstalled(installPath))
        {
            return installPath;
        }

        var fallbackHit = NuGetGlobalCache.ProbeFallbackFolders(_fallbackFolders, packageId, packageVersion);
        if (fallbackHit is not null)
        {
            return fallbackHit;
        }

        var lockPath = PackageInstallLock.GetLockFilePath(_globalPackagesFolder, installPath);
        await PackageInstallLock.RunUnderLockAsync(
            lockPath,
            alreadyDone: () => NuGetGlobalCache.IsPackageInstalled(installPath),
            work: ct => NuGetInstallHelpers.InstallFromSourcesAsync(
                _enabledSources,
                _credentials,
                _feedHttp,
                _logger,
                _flatContainerByFeed,
                packageId,
                packageVersion,
                installPath,
                ct).AsTask(),
            cancellationToken).ConfigureAwait(false);

        return installPath;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_ownsFeedHttp)
        {
            return;
        }

        _feedHttp.Dispose();
    }
}
