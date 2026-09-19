// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NuGetSettings = NuGet.Configuration.Settings;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>Acquires documentation roots using the caller's NuGet configuration.</summary>
internal sealed class PackageRestoreSession : IDisposable
{
    /// <summary>Repositories permitted by the effective configuration.</summary>
    private readonly SourceRepository[] _repositories;

    /// <summary>Source mappings applied to each root lookup.</summary>
    private readonly PackageSourceMapping _mapping;

    /// <summary>Initializes a new instance of the <see cref="PackageRestoreSession"/> class.</summary>
    /// <param name="rootDirectory">Directory whose NuGet configuration applies.</param>
    /// <param name="logger">Destination for restore diagnostics.</param>
    public PackageRestoreSession(string rootDirectory, ILogger logger)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        Settings = NuGetSettings.LoadDefaultSettings(rootDirectory);
        var provider = new PackageSourceProvider(Settings);
        PackageSource[] sources = [.. provider.LoadPackageSources()];
        var enabled = new List<PackageSource>(sources.Length);
        for (var i = 0; i < sources.Length; i++)
        {
            if (sources[i].IsEnabled)
            {
                enabled.Add(sources[i]);
            }
        }

        Sources = enabled;
        _repositories = new SourceRepository[enabled.Count];
        for (var i = 0; i < enabled.Count; i++)
        {
            _repositories[i] = Repository.Factory.GetCoreV3(enabled[i]);
        }

        _mapping = PackageSourceMapping.GetPackageSourceMapping(Settings);
        Logger = new(logger);
        PackagesPath = SettingsUtility.GetGlobalPackagesFolder(Settings);
    }

    /// <summary>Gets the effective restore settings.</summary>
    public ISettings Settings { get; }

    /// <summary>Gets the directory that supplies NuGet configuration.</summary>
    public string RootDirectory { get; }

    /// <summary>Gets the enabled package sources.</summary>
    public List<PackageSource> Sources { get; }

    /// <summary>Gets the normal global package directory.</summary>
    public string PackagesPath { get; }

    /// <summary>Gets the cache shared by requests within a discovery.</summary>
    public SourceCacheContext Cache { get; } = new();

    /// <summary>Gets the NuGet diagnostic logger.</summary>
    public PackageRestoreLogger Logger { get; }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() => Cache.Dispose();

    /// <summary>Acquires the requested documentation root or explicit reference package.</summary>
    /// <param name="id">Package identifier.</param>
    /// <param name="version">Exact version or NuGet range; null selects the latest stable root.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The package reader and its owning download result.</returns>
    /// <exception cref="InvalidOperationException">The package cannot be resolved from the configured sources.</exception>
    internal async Task<DownloadResourceResult> DownloadAsync(string id, string? version, CancellationToken cancellationToken)
    {
        var repositories = GetRepositories(id);
        var selected = await ResolveVersionAsync(repositories, id, version, cancellationToken).ConfigureAwait(false);
        var identity = new PackageIdentity(id, selected);
        if (GlobalPackagesFolderUtility.GetPackage(identity, PackagesPath) is { } cached)
        {
            return cached;
        }

        var context = new PackageDownloadContext(Cache, PackagesPath, false, _mapping) { ClientPolicyContext = ClientPolicyContext.GetClientPolicy(Settings, Logger), };
        for (var i = 0; i < repositories.Count; i++)
        {
            var resource = await repositories[i].GetResourceAsync<DownloadResource>(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"NuGet source '{repositories[i].PackageSource.Name}' does not support downloads.");
            var result = await resource.GetDownloadResourceResultAsync(identity, context, PackagesPath, Logger, cancellationToken).ConfigureAwait(false);
            if (result.Status is DownloadResourceResultStatus.Available)
            {
                using (result)
                {
                    return await InstallAsync(result, identity, repositories[i].PackageSource.Source, context, cancellationToken).ConfigureAwait(false);
                }
            }

            result.Dispose();
        }

        throw new InvalidOperationException($"NuGet could not download documentation package '{identity}'. Check configured feeds and package source mappings.");
    }

    /// <summary>Finds available versions of a reference-pack or manifest package.</summary>
    /// <param name="id">Package identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Available versions from mapped package sources.</returns>
    internal async Task<NuGetVersion[]> GetVersionsAsync(string id, CancellationToken cancellationToken)
    {
        var versions = new HashSet<NuGetVersion>();
        var repositories = GetRepositories(id);
        for (var i = 0; i < repositories.Count; i++)
        {
            var resource = await repositories[i].GetResourceAsync<FindPackageByIdResource>(cancellationToken).ConfigureAwait(false);
            if (resource is not null)
            {
                versions.UnionWith(await resource.GetAllVersionsAsync(id, Cache, Logger, cancellationToken).ConfigureAwait(false));
            }
        }

        return [.. versions];
    }

    /// <summary>Discovers package identifiers within a reference-manifest family.</summary>
    /// <param name="prefix">Package identifier prefix.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching package identifiers allowed by source mappings.</returns>
    internal async Task<string[]> FindPackageIdsAsync(string prefix, CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _repositories.Length; i++)
        {
            var repository = _repositories[i];
            var resource = await repository.GetResourceAsync<AutoCompleteResource>(cancellationToken).ConfigureAwait(false);
            if (resource is null)
            {
                continue;
            }

            var matches = await resource.IdStartsWith(prefix, true, Logger, cancellationToken).ConfigureAwait(false);
            foreach (var id in matches)
            {
                if (IsSourceAllowed(id, repository.PackageSource.Name))
                {
                    _ = ids.Add(id);
                }
            }
        }

        return [.. ids];
    }

    /// <summary>Reads framework compatibility metadata without downloading a targeting-pack archive.</summary>
    /// <param name="id">Package identifier.</param>
    /// <param name="version">Exact package version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The package's declared dependency framework groups.</returns>
    internal async Task<PackageDependencyGroup[]> GetDependencyGroupsAsync(string id, NuGetVersion version, CancellationToken cancellationToken)
    {
        var identity = new PackageIdentity(id, version);
        using var cached = GlobalPackagesFolderUtility.GetPackage(identity, PackagesPath);
        if (cached?.PackageReader is { } reader)
        {
            return [.. reader.GetPackageDependencies()];
        }

        var repositories = GetRepositories(id);
        for (var i = 0; i < repositories.Count; i++)
        {
            var resource = await repositories[i].GetResourceAsync<PackageMetadataResource>(cancellationToken).ConfigureAwait(false);
            if (resource is null)
            {
                continue;
            }

            var info = await resource.GetMetadataAsync(identity, Cache, Logger, cancellationToken).ConfigureAwait(false);
            if (info is not null)
            {
                return [.. info.DependencySets];
            }
        }

        return [];
    }

    /// <summary>Selects the configured sources permitted for a package.</summary>
    /// <param name="id">Package identifier.</param>
    /// <returns>Mapped repositories in configuration order.</returns>
    private List<SourceRepository> GetRepositories(string id)
    {
        var repositories = new List<SourceRepository>(_repositories.Length);
        for (var i = 0; i < _repositories.Length; i++)
        {
            if (IsSourceAllowed(id, _repositories[i].PackageSource.Name))
            {
                repositories.Add(_repositories[i]);
            }
        }

        return repositories;
    }

    /// <summary>Checks package-source mapping for one feed.</summary>
    /// <param name="id">Package identifier.</param>
    /// <param name="source">Configured source name.</param>
    /// <returns>True when the feed is permitted for the package.</returns>
    private bool IsSourceAllowed(string id, string source)
    {
        if (!_mapping.IsEnabled)
        {
            return true;
        }

        var allowed = _mapping.GetConfiguredPackageSources(id);
        for (var i = 0; i < allowed.Count; i++)
        {
            if (source.Equals(allowed[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Installs an acquired archive into NuGet's global package directory.</summary>
    /// <param name="download">Acquired package archive.</param>
    /// <param name="identity">Resolved package identity.</param>
    /// <param name="source">Package source.</param>
    /// <param name="context">NuGet acquisition policy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The installed package reader.</returns>
    /// <exception cref="InvalidOperationException">The source did not supply an archive.</exception>
    private async Task<DownloadResourceResult> InstallAsync(
        DownloadResourceResult download,
        PackageIdentity identity,
        string source,
        PackageDownloadContext context,
        CancellationToken cancellationToken)
    {
        var stream = download.PackageStream ?? throw new InvalidOperationException($"NuGet did not supply an archive for '{identity}'.");
        return await GlobalPackagesFolderUtility.AddPackageAsync(
            download.PackageSource ?? source,
            identity,
            stream,
            PackagesPath,
            Guid.Empty,
            context.ClientPolicyContext,
            Logger,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Selects a root version using NuGet range and floating-version rules.</summary>
    /// <param name="repositories">Mapped package sources.</param>
    /// <param name="id">Package identifier.</param>
    /// <param name="version">Requested version or range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The selected package version.</returns>
    /// <exception cref="InvalidOperationException">The requested package version is unavailable.</exception>
    private async Task<NuGetVersion> ResolveVersionAsync(List<SourceRepository> repositories, string id, string? version, CancellationToken cancellationToken)
    {
        if (NuGetVersion.TryParse(version, out var exact))
        {
            return exact;
        }

        var range = version is null ? VersionRange.Parse("*") : PackageGraphRestore.ParsePin(version);
        var versions = new HashSet<NuGetVersion>();
        if (!range.IsFloating)
        {
            var local = new FindLocalPackagesResourceV3(PackagesPath);
            foreach (var package in local.FindPackagesById(id, Logger, cancellationToken))
            {
                using var cached = GlobalPackagesFolderUtility.GetPackage(package.Identity, PackagesPath);
                if (cached is null)
                {
                    continue;
                }

                _ = versions.Add(package.Identity.Version);
            }
        }

        for (var i = 0; i < repositories.Count; i++)
        {
            var repository = repositories[i];
            var resource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"NuGet source '{repository.PackageSource.Name}' does not support package lookup.");
            versions.UnionWith(await resource.GetAllVersionsAsync(id, Cache, Logger, cancellationToken).ConfigureAwait(false));
        }

        return range.FindBestMatch(versions)
            ?? throw new InvalidOperationException($"NuGet could not resolve documentation package '{id}' with version '{range}'. Check configured feeds and package source mappings.");
    }
}
