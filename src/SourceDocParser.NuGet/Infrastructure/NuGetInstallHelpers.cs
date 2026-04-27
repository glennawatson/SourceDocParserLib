// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SourceDocParser.NuGet.Models;
using SourceDocParser.NuGet.Readers;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>
/// Helper methods for installing NuGet packages.
/// </summary>
internal static partial class NuGetInstallHelpers
{
    /// <summary>Env-var override for tests: forces every source's flat-container endpoint to a single URL.</summary>
    private const string FlatContainerEnvVar = "NUGET_FLAT_CONTAINER_OVERRIDE";

    /// <summary>SDK-recognised marker file written after a successful install.</summary>
    private const string NupkgMetadataFileName = ".nupkg.metadata";

    /// <summary>Writes the SDK-compatible <c>.nupkg.metadata</c> marker so other tools see the install as valid.</summary>
    /// <param name="installPath">Per-package directory.</param>
    /// <param name="nupkgPath">Downloaded .nupkg whose hash the marker carries.</param>
    /// <param name="source">Source the package came from.</param>
    /// <param name="cancellationToken">Token observed across the file IO.</param>
    /// <returns>A task representing the write.</returns>
    public static ValueTask WriteNupkgMetadataAsync(string installPath, string nupkgPath, in PackageSource source, CancellationToken cancellationToken) =>
        WriteNupkgMetadataCoreAsync(installPath, nupkgPath, source, cancellationToken);

    /// <summary>Implementation of <see cref="WriteNupkgMetadataAsync"/>.</summary>
    /// <param name="installPath">Per-package directory.</param>
    /// <param name="nupkgPath">Downloaded .nupkg whose hash the marker carries.</param>
    /// <param name="source">Source the package came from.</param>
    /// <param name="cancellationToken">Token observed across the file IO.</param>
    /// <returns>A task representing the write.</returns>
    public static async ValueTask WriteNupkgMetadataCoreAsync(string installPath, string nupkgPath, PackageSource source, CancellationToken cancellationToken)
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
    public static async ValueTask<string> ComputeContentHashAsync(string nupkgPath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(nupkgPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, FileOptions.SequentialScan | FileOptions.Asynchronous);
        var hash = await SHA512.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToBase64String(hash);
    }

    /// <summary>Tries each enabled source until one returns the package, then extracts it into <paramref name="installPath"/>.</summary>
    /// <param name="enabledSources">Resolved enabled sources (after disabled-set filter).</param>
    /// <param name="credentials">Per-source credentials.</param>
    /// <param name="feedHttp">HTTP surface used for service-index reads and .nupkg downloads.</param>
    /// <param name="logger">Logger threaded through to the discovery / lock helpers.</param>
    /// <param name="flatContainerByFeed">Per-source flat-container endpoint cache (one HTTP roundtrip per feed).</param>
    /// <param name="packageId">NuGet package id.</param>
    /// <param name="packageVersion">Normalised version string.</param>
    /// <param name="installPath">Per-package directory under the global cache root.</param>
    /// <param name="cancellationToken">Token observed across the network and zip work.</param>
    /// <returns>A task representing the asynchronous install.</returns>
    public static async ValueTask InstallFromSourcesAsync(
        PackageSource[] enabledSources,
        Dictionary<string, PackageSourceCredential> credentials,
        INuGetFeedHttpClient feedHttp,
        ILogger logger,
        Dictionary<string, string?> flatContainerByFeed,
        string packageId,
        string packageVersion,
        string installPath,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < enabledSources.Length; i++)
        {
            var source = enabledSources[i];
            try
            {
                if (await TryInstallFromSourceAsync(
                        source,
                        credentials,
                        feedHttp,
                        logger,
                        flatContainerByFeed,
                        packageId,
                        packageVersion,
                        installPath,
                        cancellationToken).ConfigureAwait(false))
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
    /// <param name="credentials">Per-source credentials.</param>
    /// <param name="feedHttp">HTTP surface used for service-index reads and .nupkg downloads.</param>
    /// <param name="logger">Logger threaded through to the discovery / lock helpers.</param>
    /// <param name="flatContainerByFeed">Per-source flat-container endpoint cache (one HTTP roundtrip per feed).</param>
    /// <param name="packageId">NuGet package id.</param>
    /// <param name="packageVersion">Normalised version string.</param>
    /// <param name="installPath">Per-package directory under the global cache root.</param>
    /// <param name="cancellationToken">Token observed across the network and zip work.</param>
    /// <returns>True when the install completed; false when the source returned 404.</returns>
    public static ValueTask<bool> TryInstallFromSourceAsync(
        in PackageSource source,
        Dictionary<string, PackageSourceCredential> credentials,
        INuGetFeedHttpClient feedHttp,
        ILogger logger,
        Dictionary<string, string?> flatContainerByFeed,
        string packageId,
        string packageVersion,
        string installPath,
        CancellationToken cancellationToken) =>
        TryInstallFromSourceCoreAsync(source, credentials, feedHttp, logger, flatContainerByFeed, packageId, packageVersion, installPath, cancellationToken);

    /// <summary>Implementation of <see cref="TryInstallFromSourceAsync"/>.</summary>
    /// <param name="source">Candidate feed.</param>
    /// <param name="credentials">Per-source credentials.</param>
    /// <param name="feedHttp">HTTP surface used for service-index reads and .nupkg downloads.</param>
    /// <param name="logger">Logger threaded through to the discovery / lock helpers.</param>
    /// <param name="flatContainerByFeed">Per-source flat-container endpoint cache (one HTTP roundtrip per feed).</param>
    /// <param name="packageId">NuGet package id.</param>
    /// <param name="packageVersion">Normalised version string.</param>
    /// <param name="installPath">Per-package directory under the global cache root.</param>
    /// <param name="cancellationToken">Token observed across the network and zip work.</param>
    /// <returns>True when the install completed; false when the source returned 404.</returns>
    public static async ValueTask<bool> TryInstallFromSourceCoreAsync(PackageSource source, Dictionary<string, PackageSourceCredential> credentials, INuGetFeedHttpClient feedHttp, ILogger logger, Dictionary<string, string?> flatContainerByFeed, string packageId, string packageVersion, string installPath, CancellationToken cancellationToken)
    {
        var flatContainer = await GetFlatContainerUrlAsync(source, credentials, feedHttp, flatContainerByFeed, cancellationToken).ConfigureAwait(false);
        if (flatContainer is null)
        {
            return false;
        }

        var idLower = packageId.ToLowerInvariant();
        var versionLower = packageVersion.ToLowerInvariant();
        var url = $"{flatContainer}{idLower}/{versionLower}/{idLower}.{versionLower}.nupkg";

        credentials.TryGetValue(source.Key, out var credential);
        var stream = await feedHttp.TryDownloadNupkgAsync(url, credential, cancellationToken).ConfigureAwait(false);
        if (stream is null)
        {
            return false;
        }

        Directory.CreateDirectory(installPath);
        var nupkgPath = Path.Combine(installPath, $"{idLower}.{versionLower}.nupkg");
        await using (stream.ConfigureAwait(false))
        await using (var file = File.Create(nupkgPath))
        {
            await stream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }

        await ZipFile.ExtractToDirectoryAsync(nupkgPath, installPath, overwriteFiles: true, cancellationToken).ConfigureAwait(false);
        await WriteNupkgMetadataAsync(installPath, nupkgPath, source, cancellationToken).ConfigureAwait(false);
        LogInstalled(logger, packageId, packageVersion, source.Key);
        return true;
    }

    /// <summary>Resolves and caches the flat-container endpoint for <paramref name="source"/>.</summary>
    /// <param name="source">Source whose service-index to read.</param>
    /// <param name="credentials">Per-source credentials.</param>
    /// <param name="feedHttp">HTTP surface used for service-index reads and .nupkg downloads.</param>
    /// <param name="flatContainerByFeed">Per-source flat-container endpoint cache (one HTTP roundtrip per feed).</param>
    /// <param name="cancellationToken">Token observed across the request.</param>
    /// <returns>The flat-container base URL; null when the source doesn't declare one.</returns>
    public static ValueTask<string?> GetFlatContainerUrlAsync(
        in PackageSource source,
        Dictionary<string, PackageSourceCredential> credentials,
        INuGetFeedHttpClient feedHttp,
        Dictionary<string, string?> flatContainerByFeed,
        CancellationToken cancellationToken)
    {
        if (flatContainerByFeed.TryGetValue(source.Key, out var cached))
        {
            return new(cached);
        }

        var envOverride = Environment.GetEnvironmentVariable(FlatContainerEnvVar);
        if (TextHelpers.HasNonWhitespace(envOverride))
        {
            var result = TextHelpers.EnsureTrailingSlash(envOverride);
            flatContainerByFeed[source.Key] = result;
            return new(result);
        }

        return GetFlatContainerUrlCoreAsync(source, credentials, feedHttp, flatContainerByFeed, cancellationToken);
    }

    /// <summary>Implementation of <see cref="GetFlatContainerUrlAsync"/>.</summary>
    /// <param name="source">Source whose service-index to read.</param>
    /// <param name="credentials">Per-source credentials.</param>
    /// <param name="feedHttp">HTTP surface used for service-index reads and .nupkg downloads.</param>
    /// <param name="flatContainerByFeed">Per-source flat-container endpoint cache (one HTTP roundtrip per feed).</param>
    /// <param name="cancellationToken">Token observed across the request.</param>
    /// <returns>The flat-container base URL; null when the source doesn't declare one.</returns>
    private static async ValueTask<string?> GetFlatContainerUrlCoreAsync(PackageSource source, Dictionary<string, PackageSourceCredential> credentials, INuGetFeedHttpClient feedHttp, Dictionary<string, string?> flatContainerByFeed, CancellationToken cancellationToken)
    {
        credentials.TryGetValue(source.Key, out var credential);
        await using var stream = await feedHttp.ReadServiceIndexAsync(source.Url, credential, cancellationToken).ConfigureAwait(false);
        var url = await NuGetServiceIndexReader.ReadFlatContainerUrlAsync(stream, cancellationToken).ConfigureAwait(false);
        flatContainerByFeed[source.Key] = url;
        return url;
    }

    /// <summary>Logs a successful per-package install.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="packageId">Package id.</param>
    /// <param name="packageVersion">Package version.</param>
    /// <param name="sourceKey">Source the package came from.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Installed {PackageId} {PackageVersion} from {SourceKey}")]
    private static partial void LogInstalled(ILogger logger, string packageId, string packageVersion, string sourceKey);
}
