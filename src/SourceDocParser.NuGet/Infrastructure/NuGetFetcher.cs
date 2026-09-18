// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDocParser.NuGet.Models;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>
/// Provides functionality to fetch and process NuGet packages for a specified
/// directory and API path. Responsible for coordinating package download,
/// extraction, and handling of related metadata.
/// </summary>
[System.Diagnostics.DebuggerDisplay("NuGetFetcher: {ToString(),nq}")]
public sealed partial class NuGetFetcher : INuGetFetcher, IDisposable
{
    /// <summary>Base URI of the NuGet v3 service index used for endpoint discovery.</summary>
    private static readonly Uri ServiceIndexUri = new("https://api.nuget.org/v3/index.json");

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task FetchPackagesAsync(string rootDirectory, string apiPath) =>
        FetchPackagesAsync(rootDirectory, apiPath, null, CancellationToken.None);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task FetchPackagesAsync(string rootDirectory, string apiPath, ILogger? logger) =>
        FetchPackagesAsync(rootDirectory, apiPath, logger, CancellationToken.None);

    /// <inheritdoc />
    public async Task FetchPackagesAsync(string rootDirectory, string apiPath, ILogger? logger, CancellationToken cancellationToken)
    {
        var path = await RestoredAssemblyManifest.GetPathAsync(rootDirectory, apiPath, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException("The documentation package declarations are missing.", Path.Combine(rootDirectory, "nuget-packages.json"));
        var groups = await RestoreGroupsAsync(rootDirectory, apiPath, logger ?? NullLogger.Instance, cancellationToken).ConfigureAwait(false);
        var current = await RestoredAssemblyManifest.GetPathAsync(rootDirectory, apiPath, cancellationToken).ConfigureAwait(false);
        if (!path.Equals(current, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Package declarations changed during restore. Repeat restore with stable inputs.");
        }

        await RestoredAssemblyManifest.WriteAsync(path, groups, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Builds the owner search URI for a single page of NuGet package results.</summary>
    /// <param name="searchEndpoint">Resolved NuGet search endpoint.</param>
    /// <param name="owner">Owner name to search for.</param>
    /// <param name="take">Page size.</param>
    /// <param name="skip">Page offset.</param>
    /// <returns>The fully-composed search URI.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Uri BuildOwnerSearchUri(Uri searchEndpoint, string owner, int take, int skip) =>
        new UriBuilder(searchEndpoint) { Query = $"q=owner:{Uri.EscapeDataString(owner)}&take={take}&skip={skip}&semVerLevel=2.0.0", }.Uri;

    /// <summary>Adds all eligible package identifiers from a NuGet owner search response.</summary>
    /// <param name="root">Root JSON element for the response document.</param>
    /// <param name="packageIds">Destination list to append package IDs to.</param>
    internal static void AddEligibleOwnerPackageIds(JsonElement root, List<string> packageIds)
    {
        foreach (var result in root.GetProperty("data"u8).EnumerateArray())
        {
            if (ShouldSkipOwnerSearchResult(result))
            {
                continue;
            }

            var id = result.GetProperty("id"u8).GetString();
            if (id is not null)
            {
                packageIds.Add(id);
            }
        }
    }

    /// <summary>Returns whether an owner-search result should be excluded from discovery.</summary>
    /// <param name="result">Result element from the NuGet search response.</param>
    /// <returns>True when the package should be ignored.</returns>
    internal static bool ShouldSkipOwnerSearchResult(JsonElement result)
    {
        // Skip deprecated packages (still listed but author-flagged).
        if (result.TryGetProperty("deprecation"u8, out _))
        {
            return true;
        }

        // Skip packages with known vulnerabilities so the docs site
        // never advertises a version a consumer should not pull.
        return result.TryGetProperty("vulnerabilities"u8, out var vulnerabilities)
            && vulnerabilities is { ValueKind: JsonValueKind.Array }
            && vulnerabilities.GetArrayLength() is > 0;
    }

    /// <summary>Fetches one owner-search page, appends eligible package IDs, and returns the total hit count.</summary>
    /// <param name="client">HTTP client used for the request.</param>
    /// <param name="url">Fully composed owner-search page URL.</param>
    /// <param name="packageIds">Destination list for eligible package IDs.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The total number of matching packages reported by the search service.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Task<int> FetchOwnerSearchPageAsync(
        HttpClient client,
        Uri url,
        List<string> packageIds,
        CancellationToken cancellationToken) => NuGetHttpRetry.ExecuteAsync(
            async ct =>
            {
                using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
                _ = response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

                AddEligibleOwnerPackageIds(doc.RootElement, packageIds);
                return doc.RootElement.GetProperty("totalHits"u8).GetInt32();
            },
            cancellationToken);

    /// <summary>Discovers every package owned by any of the configured NuGet owner accounts.</summary>
    /// <param name="client">Shared HTTP client for the search-service calls.</param>
    /// <param name="config">The parsed package configuration.</param>
    /// <param name="logger">Logger for endpoint discovery and per-owner counts.</param>
    /// <param name="cancellationToken">Cancellation token honoured between owner queries and per HTTP call.</param>
    /// <returns>A list of (id, version) tuples; versions are <see langword="null"/> at this stage and resolved later.</returns>
    /// <remarks>
    /// Consults the NuGet search service for each owner defined in the configuration.
    /// </remarks>
    private static async Task<List<(string Id, string? Version)>> DiscoverAllPackagesAsync(HttpClient client, PackageConfig config, ILogger logger, CancellationToken cancellationToken)
    {
        if (config.NugetPackageOwners is [])
        {
            return [];
        }

        var searchEndpoint = await ResolveSearchEndpointAsync(client, cancellationToken).ConfigureAwait(false);
        LogUsingSearchEndpoint(logger, searchEndpoint);

        List<(string Id, string? Version)> allIds = [];
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < config.NugetPackageOwners.Length; i++)
        {
            var owner = config.NugetPackageOwners[i];
            cancellationToken.ThrowIfCancellationRequested();
            var ids = await DiscoverPackagesByOwnerAsync(client, searchEndpoint, owner, cancellationToken).ConfigureAwait(false);
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

    /// <summary>Reads the NuGet v3 service index and returns the URI of the search service.</summary>
    /// <param name="client">HTTP client used for the request.</param>
    /// <param name="cancellationToken">Cancellation token honoured by the HTTP request.</param>
    /// <returns>The resolved search endpoint URI.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the service index does not advertise the required search service type.</exception>
    private static async Task<Uri> ResolveSearchEndpointAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var endpoint = await NuGetHttpRetry.ExecuteAsync(
            async ct =>
            {
                using var response = await client.GetAsync(ServiceIndexUri, ct).ConfigureAwait(false);
                _ = response.EnsureSuccessStatusCode();

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
                        return new Uri(id);
                    }

                    break;
                }

                return null;
            },
            cancellationToken).ConfigureAwait(false);

        return endpoint ?? throw new InvalidOperationException(
            $"Could not find {Encoding.UTF8.GetString("SearchQueryService/3.5.0"u8)} in NuGet service index");
    }

    /// <summary>Pages through the NuGet search service to enumerate every package owned by the supplied owner.</summary>
    /// <param name="client">HTTP client used for the requests.</param>
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
            var url = BuildOwnerSearchUri(searchEndpoint, owner, take, skip);
            var totalHits = await FetchOwnerSearchPageAsync(client, url, packageIds, cancellationToken).ConfigureAwait(false);

            skip += take;
            if (skip >= totalHits)
            {
                break;
            }
        }

        return packageIds;
    }

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
}
