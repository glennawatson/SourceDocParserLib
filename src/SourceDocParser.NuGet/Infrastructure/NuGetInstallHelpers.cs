// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SourceDocParser.NuGet.Models;
using SourceDocParser.NuGet.Readers;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>
/// Helper methods for installing NuGet packages.
/// </summary>
[SuppressMessage("Minor Code Smell", "S4040:Strings should be normalized to uppercase", Justification = "NuGet flat-container package IDs and versions are lower-case in feed URLs")]
internal static partial class NuGetInstallHelpers
{
    /// <summary>Env-var override for tests: forces every source's flat-container endpoint to a single URL.</summary>
    private const string FlatContainerEnvVar = "NUGET_FLAT_CONTAINER_OVERRIDE";

    /// <summary>SDK-recognised marker file written after a successful install.</summary>
    private const string NupkgMetadataFileName = ".nupkg.metadata";

    /// <summary>The version of the <c>.nupkg.metadata</c> file.</summary>
    private const int NupkgMetadataVersion = 2;

    /// <summary>The buffer size for writing the <c>.nupkg.metadata</c> file.</summary>
    private const int MetadataWriteBufferSize = 256;

    /// <summary>The buffer size for computing the content hash.</summary>
    private const int HashBufferSize = 81920;

    /// <summary>The extension for NuGet packages.</summary>
    private const string NupkgExtension = ".nupkg";

    /// <summary>Writes the SDK-compatible <c>.nupkg.metadata</c> marker so other tools see the install as valid.</summary>
    /// <param name="installPath">Per-package directory.</param>
    /// <param name="nupkgPath">Downloaded .nupkg whose hash the marker carries.</param>
    /// <param name="source">Source the package came from.</param>
    /// <param name="cancellationToken">Token observed across the file IO.</param>
    /// <returns>A task representing the write.</returns>
    public static ValueTask WriteNupkgMetadataAsync(string installPath, string nupkgPath, in PackageSource source, in CancellationToken cancellationToken) =>
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
        await using var stream = new FileStream(metadataPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: MetadataWriteBufferSize, FileOptions.Asynchronous);
        await using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteNumber("version"u8, NupkgMetadataVersion);
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
        await using var stream = new FileStream(nupkgPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: HashBufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous);
        var hash = await SHA512.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToBase64String(hash);
    }

    /// <summary>Installs a NuGet package asynchronously from the specified sources.</summary>
    /// <param name="request">The installation request containing details such as package information, sources, credentials, and cancellation token.</param>
    /// <returns>A task representing the asynchronous installation operation.</returns>
    public static ValueTask InstallFromSourcesAsync(in NuGetInstallRequest request) =>
        InstallFromSourcesCoreAsync(request);

    /// <summary>Implementation of <see cref="InstallFromSourcesAsync(in NuGetInstallRequest)"/>.</summary>
    /// <param name="request">Install request state.</param>
    /// <returns>A task representing the asynchronous install.</returns>
    public static async ValueTask InstallFromSourcesCoreAsync(NuGetInstallRequest request)
    {
        var enabledSources = request.EnabledSources;
        for (var i = 0; i < enabledSources.Length; i++)
        {
            var source = enabledSources[i];
            try
            {
                if (await TryInstallFromSourceAsync(source, request).ConfigureAwait(false))
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Try the next source.
            }
        }

        throw new InvalidOperationException(
            $"Package {request.PackageId} {request.PackageVersion} not found in any configured source.");
    }

    /// <summary>Attempts to install a package from the specified source.</summary>
    /// <param name="source">The package source containing the URL and key of the source feed.</param>
    /// <param name="request">The installation request containing package details and configurations needed for installation.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains a boolean value indicating whether the installation was successful.</returns>
    public static ValueTask<bool> TryInstallFromSourceAsync(in PackageSource source, in NuGetInstallRequest request) =>
        TryInstallFromSourceCoreAsync(source, request);

    /// <summary>
    /// Attempts to install a package from the specified NuGet source.
    /// </summary>
    /// <param name="source">The NuGet package source to install from.</param>
    /// <param name="request">Details of the installation request, including package information, HTTP client, and credentials.</param>
    /// <returns>A <c>ValueTask</c> representing the asynchronous operation, with a boolean result indicating whether the installation succeeded.</returns>
    public static async ValueTask<bool> TryInstallFromSourceCoreAsync(
        PackageSource source,
        NuGetInstallRequest request)
    {
        var flatContainer = await GetFlatContainerUrlAsync(
            source,
            request.Credentials,
            request.FeedHttp,
            request.FlatContainerByFeed,
            request.CancellationToken).ConfigureAwait(false);
        if (flatContainer is null)
        {
            return false;
        }

        var idLower = request.PackageId.ToLowerInvariant();
        var versionLower = request.PackageVersion.ToLowerInvariant();
        var url = $"{flatContainer}{idLower}/{versionLower}/{idLower}.{versionLower}{NupkgExtension}";

        request.Credentials.TryGetValue(source.Key, out var credential);
        var stream = await request.FeedHttp.TryDownloadNupkgAsync(
            url,
            credential,
            request.CancellationToken).ConfigureAwait(false);
        if (stream is null)
        {
            return false;
        }

        Directory.CreateDirectory(request.InstallPath);
        var nupkgPath = Path.Combine(request.InstallPath, $"{idLower}.{versionLower}{NupkgExtension}");
        await using (stream.ConfigureAwait(false))
        await using (var file = File.Create(nupkgPath))
        {
            await stream.CopyToAsync(file, request.CancellationToken).ConfigureAwait(false);
        }

        await ZipFile.ExtractToDirectoryAsync(
            nupkgPath,
            request.InstallPath,
            overwriteFiles: true,
            request.CancellationToken).ConfigureAwait(false);
        await WriteNupkgMetadataAsync(request.InstallPath, nupkgPath, source, request.CancellationToken).ConfigureAwait(false);
        LogInstalled(request.Logger, request.PackageId, request.PackageVersion, source.Key);
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
        in CancellationToken cancellationToken)
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
    private static async ValueTask<string?> GetFlatContainerUrlCoreAsync(
        PackageSource source,
        Dictionary<string, PackageSourceCredential> credentials,
        INuGetFeedHttpClient feedHttp,
        Dictionary<string, string?> flatContainerByFeed,
        CancellationToken cancellationToken)
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
