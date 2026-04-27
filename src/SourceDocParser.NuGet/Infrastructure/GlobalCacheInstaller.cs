// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDocParser.NuGet.Models;
using SourceDocParser.NuGet.Readers;

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
public sealed partial class GlobalCacheInstaller : IDisposable
{
    /// <summary>Env-var override for tests: forces every source's flat-container endpoint to a single URL.</summary>
    private const string FlatContainerEnvVar = "NUGET_FLAT_CONTAINER_OVERRIDE";

    /// <summary>SDK-recognised marker file written after a successful install.</summary>
    private const string NupkgMetadataFileName = ".nupkg.metadata";

    /// <summary>Project root used to start the nuget.config discovery walk.</summary>
    private readonly string _workingFolder;

    /// <summary>Logger threaded through to the discovery / lock helpers.</summary>
    private readonly ILogger _logger;

    /// <summary>HTTP client reused across downloads.</summary>
    private readonly HttpClient _http;

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
    /// <param name="http">Optional HTTP client; pass-in lets tests stub the network.</param>
    public GlobalCacheInstaller(string workingFolder, ILogger? logger = null, HttpClient? http = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingFolder);
        _workingFolder = workingFolder;
        _logger = logger ?? NullLogger.Instance;
        _http = http ?? new HttpClient();
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
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
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
    public async Task<string> InstallAsync(string packageId, string packageVersion, CancellationToken cancellationToken = default)
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

        var fallbackHit = ProbeFallbackFolders(packageId, packageVersion);
        if (fallbackHit is not null)
        {
            return fallbackHit;
        }

        var lockPath = PackageInstallLock.GetLockFilePath(_globalPackagesFolder, installPath);
        await PackageInstallLock.RunUnderLockAsync(
            lockPath,
            alreadyDone: () => NuGetGlobalCache.IsPackageInstalled(installPath),
            work: ct => InstallFromSourcesAsync(packageId, packageVersion, installPath, ct),
            cancellationToken).ConfigureAwait(false);

        return installPath;
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    /// <summary>Writes the SDK-compatible <c>.nupkg.metadata</c> marker so other tools see the install as valid.</summary>
    /// <param name="installPath">Per-package directory.</param>
    /// <param name="nupkgPath">Downloaded .nupkg whose hash the marker carries.</param>
    /// <param name="source">Source the package came from.</param>
    /// <param name="cancellationToken">Token observed across the file IO.</param>
    /// <returns>A task representing the write.</returns>
    private static async Task WriteNupkgMetadataAsync(string installPath, string nupkgPath, PackageSource source, CancellationToken cancellationToken)
    {
        var hash = await ComputeContentHashAsync(nupkgPath, cancellationToken).ConfigureAwait(false);
        var metadataPath = Path.Combine(installPath, NupkgMetadataFileName);
        await using var stream = new FileStream(metadataPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 256, FileOptions.Asynchronous);
        await using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteNumber("version"u8, 2);
        writer.WriteString("contentHash"u8, hash);
        writer.WriteString("source"u8, source.Url);
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Computes the SHA-512 / Base64 content hash NuGet stores in <c>.nupkg.metadata</c>.</summary>
    /// <param name="nupkgPath">Path to the .nupkg.</param>
    /// <param name="cancellationToken">Token observed across the read.</param>
    /// <returns>Base64-encoded SHA-512 of the .nupkg bytes.</returns>
    private static async Task<string> ComputeContentHashAsync(string nupkgPath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(nupkgPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, FileOptions.SequentialScan | FileOptions.Asynchronous);
        var hash = await SHA512.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToBase64String(hash);
    }

    /// <summary>Logs a successful per-package install.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="packageId">Package id.</param>
    /// <param name="packageVersion">Package version.</param>
    /// <param name="sourceKey">Source the package came from.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Installed {PackageId} {PackageVersion} from {SourceKey}")]
    private static partial void LogInstalled(ILogger logger, string packageId, string packageVersion, string sourceKey);

    /// <summary>Probes each configured fallback folder for an already-extracted install.</summary>
    /// <param name="packageId">NuGet package id.</param>
    /// <param name="packageVersion">Normalised version string.</param>
    /// <returns>The fallback install path when found; null otherwise.</returns>
    private string? ProbeFallbackFolders(string packageId, string packageVersion)
    {
        for (var i = 0; i < _fallbackFolders!.Length; i++)
        {
            var candidate = NuGetGlobalCache.GetPackageInstallPath(_fallbackFolders[i], packageId, packageVersion);
            if (NuGetGlobalCache.IsPackageInstalled(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Tries each enabled source until one returns the package, then extracts it into <paramref name="installPath"/>.</summary>
    /// <param name="packageId">NuGet package id.</param>
    /// <param name="packageVersion">Normalised version string.</param>
    /// <param name="installPath">Per-package directory under the global cache root.</param>
    /// <param name="cancellationToken">Token observed across the network and zip work.</param>
    /// <returns>A task representing the asynchronous install.</returns>
    private async Task InstallFromSourcesAsync(string packageId, string packageVersion, string installPath, CancellationToken cancellationToken)
    {
        for (var i = 0; i < _enabledSources!.Length; i++)
        {
            var source = _enabledSources[i];
            try
            {
                if (await TryInstallFromSourceAsync(source, packageId, packageVersion, installPath, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Try the next source.
            }
        }

        throw new InvalidOperationException($"Package {packageId} {packageVersion} not found in any configured source.");
    }

    /// <summary>Downloads + extracts <paramref name="packageId"/> from <paramref name="source"/>.</summary>
    /// <param name="source">Candidate feed.</param>
    /// <param name="packageId">NuGet package id.</param>
    /// <param name="packageVersion">Normalised version string.</param>
    /// <param name="installPath">Per-package directory under the global cache root.</param>
    /// <param name="cancellationToken">Token observed across the network and zip work.</param>
    /// <returns>True when the install completed; false when the source returned 404.</returns>
    private async Task<bool> TryInstallFromSourceAsync(
        PackageSource source,
        string packageId,
        string packageVersion,
        string installPath,
        CancellationToken cancellationToken)
    {
        var flatContainer = await GetFlatContainerUrlAsync(source, cancellationToken).ConfigureAwait(false);
        if (flatContainer is null)
        {
            return false;
        }

        var idLower = packageId.ToLowerInvariant();
        var versionLower = packageVersion.ToLowerInvariant();
        var url = $"{flatContainer}{idLower}/{versionLower}/{idLower}.{versionLower}.nupkg";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyCredentials(request, source);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();

        Directory.CreateDirectory(installPath);
        var nupkgPath = Path.Combine(installPath, $"{idLower}.{versionLower}.nupkg");
        await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var file = File.Create(nupkgPath))
        {
            await stream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }

        await ZipFile.ExtractToDirectoryAsync(nupkgPath, installPath, overwriteFiles: true, cancellationToken).ConfigureAwait(false);
        await WriteNupkgMetadataAsync(installPath, nupkgPath, source, cancellationToken).ConfigureAwait(false);
        LogInstalled(_logger, packageId, packageVersion, source.Key);
        return true;
    }

    /// <summary>Resolves and caches the flat-container endpoint for <paramref name="source"/>.</summary>
    /// <param name="source">Source whose service-index to read.</param>
    /// <param name="cancellationToken">Token observed across the request.</param>
    /// <returns>The flat-container base URL; null when the source doesn't declare one.</returns>
    private async Task<string?> GetFlatContainerUrlAsync(PackageSource source, CancellationToken cancellationToken)
    {
        if (_flatContainerByFeed.TryGetValue(source.Key, out var cached))
        {
            return cached;
        }

        var envOverride = Environment.GetEnvironmentVariable(FlatContainerEnvVar);
        if (TextHelpers.HasNonWhitespace(envOverride))
        {
            _flatContainerByFeed[source.Key] = TextHelpers.EnsureTrailingSlash(envOverride);
            return _flatContainerByFeed[source.Key];
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
        ApplyCredentials(request, source);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var url = await NuGetServiceIndexReader.ReadFlatContainerUrlAsync(stream, cancellationToken).ConfigureAwait(false);
        _flatContainerByFeed[source.Key] = url;
        return url;
    }

    /// <summary>Adds Basic-auth header to <paramref name="request"/> when credentials are configured for <paramref name="source"/>.</summary>
    /// <param name="request">Outgoing request.</param>
    /// <param name="source">Source we're talking to.</param>
    private void ApplyCredentials(HttpRequestMessage request, PackageSource source)
    {
        if (!_credentials!.TryGetValue(source.Key, out var cred))
        {
            return;
        }

        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{cred.Username}:{cred.ClearTextPassword}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }
}
