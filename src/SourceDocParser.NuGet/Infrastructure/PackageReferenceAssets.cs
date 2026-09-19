// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging;
using NuGet.ProjectModel;
using NuGet.Versioning;
using SourceDocParser.NuGet.Models;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>Supplies explicit package constraints and target-framework reference assemblies.</summary>
internal static class PackageReferenceAssets
{
    /// <summary>The .NET Standard compatibility package.</summary>
    private const string StandardLibrary = "NETStandard.Library";

    /// <summary>The reference package family for .NET Framework.</summary>
    private const string FrameworkReferences = "Microsoft.NETFramework.ReferenceAssemblies";

    /// <summary>The package supplying the base .NET reference assemblies.</summary>
    private const string CoreReferencePack = "Microsoft.NETCore.App.Ref";

    /// <summary>Adds targeting-pack downloads while preserving the root's package dependency constraints.</summary>
    /// <param name="session">Configured package acquisition session.</param>
    /// <param name="config">Reference declarations and dependency pins.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="dependencies">Direct constraints, including the documentation root.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Targeting packs required by the restore.</returns>
    internal static async Task<List<DownloadDependency>> AddReferencesAsync(
        PackageRestoreSession session,
        PackageConfig config,
        NuGetFramework framework,
        List<LibraryDependency> dependencies,
        CancellationToken cancellationToken)
    {
        var downloads = new List<DownloadDependency>(config.ReferencePackages.Length);
        var selected = SelectReferences(config.ReferencePackages, framework);
        for (var i = 0; i < selected.Length; i++)
        {
            var reference = selected[i];
            if (!IsFrameworkPackReference(reference) || ContainsDependency(dependencies, reference.Id))
            {
                continue;
            }

            using var download = await session.DownloadAsync(reference.Id, reference.Version, cancellationToken).ConfigureAwait(false);
            var reader = download.PackageReader!;
            var identity = reader.GetIdentity();
            downloads.Add(new(identity.Id, ExactVersion(identity.Version)));
        }

        await FrameworkReferencePackages.AddDownloadsAsync(session, framework, dependencies, downloads, cancellationToken).ConfigureAwait(false);
        await PlatformReferencePackages.AddDownloadsAsync(session, framework, downloads, cancellationToken).ConfigureAwait(false);
        return downloads;
    }

    /// <summary>Adds framework references while retaining the graph's selected package assets.</summary>
    /// <param name="session">Configured package acquisition session.</param>
    /// <param name="config">Reference declarations and dependency pins.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="rootId">The package whose APIs are documented.</param>
    /// <param name="references">References selected by restore.</param>
    /// <param name="assets">Resolved package and framework declarations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing reference discovery.</returns>
    internal static async Task AddFrameworkAssetsAsync(
        PackageRestoreSession session,
        PackageConfig config,
        NuGetFramework framework,
        string rootId,
        Dictionary<string, string> references,
        LockFile assets,
        CancellationToken cancellationToken)
    {
        var selected = SelectReferences(config.ReferencePackages, framework);
        var frameworkAssets = new FrameworkAssetSet([with(StringComparer.OrdinalIgnoreCase)], [with(StringComparer.OrdinalIgnoreCase)]);
        await AddExplicitFrameworkAssetsAsync(session, selected, assets, frameworkAssets, false, cancellationToken).ConfigureAwait(false);
        await AddDeclaredFrameworkPacksAsync(session, config, framework, assets, frameworkAssets, cancellationToken).ConfigureAwait(false);
        await AddExplicitFrameworkAssetsAsync(session, selected, assets, frameworkAssets, true, cancellationToken).ConfigureAwait(false);
        await AddDownloadedPacksAsync(session, framework, assets, frameworkAssets, cancellationToken).ConfigureAwait(false);
        await AddImplicitFrameworkAssetsAsync(session, config, framework, assets, frameworkAssets, cancellationToken).ConfigureAwait(false);
        if (assets.GetTarget(framework, config.RuntimeIdentifier ?? string.Empty) is { } target)
        {
            ReferencePackageOverrides.Apply(target, rootId, frameworkAssets.References, frameworkAssets.Overrides, references);
        }

        foreach (var reference in frameworkAssets.References)
        {
            _ = references.TryAdd(reference.Key, reference.Value);
        }
    }

    /// <summary>Selects one compatible framework declaration per reference package.</summary>
    /// <param name="references">Explicit reference declarations.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <returns>The declarations for the nearest compatible framework groups.</returns>
    internal static ReferencePackage[] SelectReferences(ReferencePackage[] references, NuGetFramework framework)
    {
        var result = new List<ReferencePackage>(references.Length);
        var reducer = new FrameworkReducer();
        for (var i = 0; i < references.Length; i++)
        {
            var candidate = references[i];
            if (!MatchesReferenceFramework(candidate, framework))
            {
                continue;
            }

            var frameworks = new List<NuGetFramework>(references.Length);
            for (var j = 0; j < references.Length; j++)
            {
                if (SameReferenceFamily(candidate.Id, references[j].Id))
                {
                    frameworks.Add(ParseReferenceFramework(references[j]));
                }
            }

            var nearest = reducer.GetNearest(framework, frameworks);
            if (nearest is not null && nearest.Equals(ParseReferenceFramework(candidate)))
            {
                result.Add(candidate);
            }
        }

        return [.. result];
    }

    /// <summary>Selects a targeting pack's compatible reference assets.</summary>
    /// <param name="packagesPath">NuGet global package directory.</param>
    /// <param name="reader">Downloaded targeting pack.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="references">Framework reference assemblies.</param>
    internal static void AddCompatiblePackReferences(string packagesPath, PackageReaderBase reader, NuGetFramework framework, Dictionary<string, string> references)
    {
        var groups = new List<FrameworkSpecificGroup>(reader.GetItems("ref"));
        if (groups.Count is 0)
        {
            groups.AddRange(reader.GetLibItems());
        }

        var frameworks = new NuGetFramework[groups.Count];
        for (var g = 0; g < groups.Count; g++)
        {
            frameworks[g] = groups[g].TargetFramework;
        }

        var nearest = new FrameworkReducer().GetNearest(framework, frameworks);
        for (var g = 0; g < groups.Count; g++)
        {
            if (groups[g].TargetFramework.Equals(nearest))
            {
                AddPackageFiles(packagesPath, reader, groups[g].Items, references);
            }
        }
    }

    /// <summary>Adds selected targeting-pack assemblies from their package installation.</summary>
    /// <param name="packagesPath">NuGet global package directory.</param>
    /// <param name="reader">Downloaded targeting pack.</param>
    /// <param name="files">Selected package-relative asset paths.</param>
    /// <param name="references">Framework reference assemblies.</param>
    /// <exception cref="FileNotFoundException">A downloaded targeting-pack assembly is absent from its installation.</exception>
    internal static void AddPackageFiles(string packagesPath, PackageReaderBase reader, IEnumerable<string> files, Dictionary<string, string> references)
    {
        var identity = reader.GetIdentity();
        var directory = new VersionFolderPathResolver(packagesPath).GetInstallPath(identity.Id, identity.Version);
        foreach (var file in files)
        {
            if (!file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = Path.GetFullPath(Path.Combine(directory, file));
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Targeting-pack assembly '{file}' from '{identity}' is absent from the NuGet package installation.", path);
            }

            using var stream = File.OpenRead(path);
            if (!ManagedAssemblyExtractor.IsManagedAssembly(stream))
            {
                continue;
            }

            _ = references.TryAdd(Path.GetFileNameWithoutExtension(file), path);
        }
    }

    /// <summary>Adds explicitly selected package assets at their framework priority.</summary>
    /// <param name="session">Configured package acquisition session.</param>
    /// <param name="selected">Applicable targeting-pack declarations.</param>
    /// <param name="assets">Resolved package graph.</param>
    /// <param name="references">Framework reference assemblies.</param>
    /// <param name="baseFrameworkOnly">True to add the base framework after specialized frameworks.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing configured targeting-pack acquisition.</returns>
    private static async Task AddExplicitFrameworkAssetsAsync(
        PackageRestoreSession session,
        ReferencePackage[] selected,
        LockFile assets,
        FrameworkAssetSet references,
        bool baseFrameworkOnly,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < selected.Length; i++)
        {
            var reference = selected[i];
            if (reference.Id.Equals(CoreReferencePack, StringComparison.OrdinalIgnoreCase) != baseFrameworkOnly)
            {
                continue;
            }

            var version = GetSelectedPackageVersion(assets, reference.Id);
            if (version is null && !IsFrameworkPackReference(reference))
            {
                continue;
            }

            version ??= reference.Version;
            using var download = await session.DownloadAsync(reference.Id, version, cancellationToken).ConfigureAwait(false);
            var reader = download.PackageReader!;
            if (reference.PathPrefix is [_, ..])
            {
                AddPackagePrefix(session.PackagesPath, reader, reference.PathPrefix, references.References);
            }

            if (IsFrameworkPackage(reference, reader.NuspecReader))
            {
                ReferencePackageOverrides.Read(reader, references.Overrides);
            }
        }
    }

    /// <summary>Finds the version selected by the dependency graph for an explicit reference.</summary>
    /// <param name="assets">Resolved dependency graph.</param>
    /// <param name="id">Reference package identifier.</param>
    /// <returns>The selected version, or null for a reference-only download.</returns>
    private static string? GetSelectedPackageVersion(LockFile assets, string id)
    {
        for (var i = 0; i < assets.Libraries.Count; i++)
        {
            if (string.Equals(assets.Libraries[i].Name, id, StringComparison.OrdinalIgnoreCase))
            {
                return assets.Libraries[i].Version.ToNormalizedString();
            }
        }

        return null;
    }

    /// <summary>Identifies declarations that select alternative frameworks for the same reference family.</summary>
    /// <param name="left">First package identifier.</param>
    /// <param name="right">Second package identifier.</param>
    /// <returns>True when the declarations supply the same reference family.</returns>
    private static bool SameReferenceFamily(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase)
        || (left.StartsWith(FrameworkReferences, StringComparison.OrdinalIgnoreCase) && right.StartsWith(FrameworkReferences, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns the framework scope of an explicit reference.</summary>
    /// <param name="reference">Explicit reference declaration.</param>
    /// <returns>The declared framework, or any framework for an unscoped reference.</returns>
    private static NuGetFramework ParseReferenceFramework(ReferencePackage reference) => reference.TargetTfm is { Length: > 0 }
        ? NuGetFramework.ParseFolder(reference.TargetTfm)
        : NuGetFramework.AnyFramework;

    /// <summary>Identifies targeting packs that cannot participate as ordinary package references.</summary>
    /// <param name="reference">Explicit reference declaration.</param>
    /// <param name="nuspec">Package metadata.</param>
    /// <returns>True for framework targeting packs.</returns>
    private static bool IsFrameworkPackage(ReferencePackage reference, NuspecReader nuspec)
    {
        if (IsFrameworkPackReference(reference))
        {
            return true;
        }

        var types = nuspec.GetPackageTypes();
        for (var i = 0; i < types.Count; i++)
        {
            if (types[i].Name.Equals("DotnetPlatform", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Identifies an explicit framework-pack declaration.</summary>
    /// <param name="reference">Explicit reference declaration.</param>
    /// <returns>True for targeting-pack names and framework reference directories.</returns>
    private static bool IsFrameworkPackReference(ReferencePackage reference) => reference.Id.EndsWith(".Ref", StringComparison.OrdinalIgnoreCase)
        || reference.Id.Contains(".Ref.", StringComparison.OrdinalIgnoreCase)
        || reference.Id.StartsWith(FrameworkReferences, StringComparison.OrdinalIgnoreCase)
        || reference.PathPrefix.StartsWith("build/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Restricts framework reference packs to their declared target framework version.</summary>
    /// <param name="reference">Explicit reference declaration.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <returns>True when the pack belongs to this framework or its unqualified base.</returns>
    private static bool MatchesReferenceFramework(ReferencePackage reference, NuGetFramework framework)
    {
        var scope = ParseReferenceFramework(reference);
        if (scope.IsAny || !IsFrameworkPackReference(reference))
        {
            return true;
        }

        return scope.Framework.Equals(framework.Framework, StringComparison.OrdinalIgnoreCase)
            && scope.Version.Equals(framework.Version)
            && (!scope.HasPlatform || scope.Equals(framework));
    }

    /// <summary>Checks whether a root or explicit dependency pin already supplies a constraint.</summary>
    /// <param name="dependencies">Direct dependency constraints.</param>
    /// <param name="id">Package identifier.</param>
    /// <returns>True when a direct constraint exists.</returns>
    private static bool ContainsDependency(List<LibraryDependency> dependencies, string id)
    {
        for (var i = 0; i < dependencies.Count; i++)
        {
            if (dependencies[i].Name.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Creates an exact download constraint.</summary>
    /// <param name="version">Selected package version.</param>
    /// <returns>The exact version range.</returns>
    private static VersionRange ExactVersion(NuGetVersion version) => new(version, true, version, true);

    /// <summary>Acquires reference packs declared by the resolved packages.</summary>
    /// <param name="session">Configured NuGet session.</param>
    /// <param name="config">Explicit framework pins.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="assets">Resolved package declarations.</param>
    /// <param name="references">Resolved assembly references.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing reference-pack acquisition.</returns>
    private static async Task AddDeclaredFrameworkPacksAsync(
        PackageRestoreSession session,
        PackageConfig config,
        NuGetFramework framework,
        LockFile assets,
        FrameworkAssetSet references,
        CancellationToken cancellationToken)
    {
        var target = assets.GetTarget(framework, config.RuntimeIdentifier ?? string.Empty);
        if (target is null)
        {
            return;
        }

        var packs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < target.Libraries.Count; i++)
        {
            var names = target.Libraries[i].FrameworkReferences;
            for (var n = 0; n < names.Count; n++)
            {
                var name = names[n];
                if (name.Equals("Microsoft.NETCore.App", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var id = name.StartsWith("Microsoft.WindowsDesktop.App", StringComparison.OrdinalIgnoreCase) ? "Microsoft.WindowsDesktop.App.Ref" : $"{name}.Ref";
                _ = packs.Add(id);
            }
        }

        var selected = SelectReferences(config.ReferencePackages, framework);
        foreach (var id in packs)
        {
            if (HasExplicitPack(selected, id))
            {
                continue;
            }

            var version = await FrameworkReferencePackages.ResolveVersionAsync(session, id, framework.Version, cancellationToken).ConfigureAwait(false);
            using var download = await session.DownloadAsync(id, version.ToNormalizedString(), cancellationToken).ConfigureAwait(false);
            AddCompatiblePackReferences(session.PackagesPath, download.PackageReader!, framework, references.References);
            ReferencePackageOverrides.Read(download.PackageReader!, references.Overrides);
        }
    }

    /// <summary>Adds the reference-pack assets acquired through NuGet.</summary>
    /// <param name="session">Configured package acquisition session.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="assets">Resolved reference-pack downloads.</param>
    /// <param name="references">Framework reference assemblies.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing reference-pack acquisition.</returns>
    private static async Task AddDownloadedPacksAsync(
        PackageRestoreSession session,
        NuGetFramework framework,
        LockFile assets,
        FrameworkAssetSet references,
        CancellationToken cancellationToken)
    {
        var targets = assets.PackageSpec.TargetFrameworks;
        for (var t = 0; t < targets.Count; t++)
        {
            if (!targets[t].FrameworkName.Equals(framework))
            {
                continue;
            }

            var downloads = targets[t].DownloadDependencies;
            for (var d = 0; d < downloads.Length; d++)
            {
                using var download = await session.DownloadAsync(downloads[d].Name, downloads[d].VersionRange.ToNormalizedString(), cancellationToken).ConfigureAwait(false);
                ReferencePackageOverrides.Read(download.PackageReader!, references.Overrides);
                if (downloads[d].Name.StartsWith(FrameworkReferences, StringComparison.OrdinalIgnoreCase))
                {
                    AddPackagePrefix(session.PackagesPath, download.PackageReader!, "build/.NETFramework", references.References);
                    continue;
                }

                AddCompatiblePackReferences(session.PackagesPath, download.PackageReader!, framework, references.References);
            }
        }
    }

    /// <summary>Checks for an explicitly configured targeting pack.</summary>
    /// <param name="references">Applicable explicit references.</param>
    /// <param name="id">Targeting-pack identifier.</param>
    /// <returns>True when the caller supplied the targeting pack.</returns>
    private static bool HasExplicitPack(ReferencePackage[] references, string id)
    {
        for (var i = 0; i < references.Length; i++)
        {
            if (references[i].Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Supplies reference assemblies carried by framework package dependencies.</summary>
    /// <param name="session">Configured package acquisition session.</param>
    /// <param name="config">Explicit dependency pins.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="assets">Resolved package versions.</param>
    /// <param name="references">Resolved assembly references.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing reference acquisition.</returns>
    /// <exception cref="InvalidOperationException">The required .NET Standard reference package is absent.</exception>
    private static async Task AddImplicitFrameworkAssetsAsync(
        PackageRestoreSession session,
        PackageConfig config,
        NuGetFramework framework,
        LockFile assets,
        FrameworkAssetSet references,
        CancellationToken cancellationToken)
    {
        if (framework.Framework is FrameworkConstants.FrameworkIdentifiers.NetStandard && framework.Version < new Version(2, 1))
        {
            var version = FindResolvedVersion(assets, framework, config.RuntimeIdentifier, StandardLibrary)
                ?? throw new InvalidOperationException($"NuGet target '{framework}' does not contain required framework reference package '{StandardLibrary}'.");
            using var download = await session.DownloadAsync(StandardLibrary, version, cancellationToken).ConfigureAwait(false);
            AddPackagePrefix(session.PackagesPath, download.PackageReader!, "build/netstandard2.0/ref", references.References);
            ReferencePackageOverrides.Read(download.PackageReader!, references.Overrides);
        }
        else if (framework.Framework is FrameworkConstants.FrameworkIdentifiers.Net)
        {
            var id = $"{FrameworkReferences}.{framework.GetShortFolderName()}";
            var version = FindResolvedVersion(assets, framework, config.RuntimeIdentifier, id);
            if (version is null)
            {
                return;
            }

            using var download = await session.DownloadAsync(id, version, cancellationToken).ConfigureAwait(false);
            AddPackagePrefix(session.PackagesPath, download.PackageReader!, "build/.NETFramework", references.References);
            ReferencePackageOverrides.Read(download.PackageReader!, references.Overrides);
        }
    }

    /// <summary>Reads an implicit framework package's version from the resolved target.</summary>
    /// <param name="assets">Resolved package versions.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="runtimeIdentifier">Optional runtime target.</param>
    /// <param name="id">Framework package identifier.</param>
    /// <returns>The exact package version, or null when the package is absent from the graph.</returns>
    private static string? FindResolvedVersion(LockFile assets, NuGetFramework framework, string? runtimeIdentifier, string id)
    {
        var target = assets.GetTarget(framework, runtimeIdentifier ?? string.Empty);
        if (target is not null)
        {
            for (var i = 0; i < target.Libraries.Count; i++)
            {
                var library = target.Libraries[i];
                if (string.Equals(library.Name, id, StringComparison.OrdinalIgnoreCase) && library.Version is { } version)
                {
                    return version.ToNormalizedString();
                }
            }
        }

        return null;
    }

    /// <summary>Adds assemblies from an explicit framework-pack prefix.</summary>
    /// <param name="packagesPath">NuGet global package directory.</param>
    /// <param name="reader">Downloaded package.</param>
    /// <param name="prefix">Package-relative reference directory.</param>
    /// <param name="references">Resolved assembly references.</param>
    /// <exception cref="FileNotFoundException">A downloaded targeting-pack assembly is absent from its installation.</exception>
    private static void AddPackagePrefix(string packagesPath, PackageReaderBase reader, string prefix, Dictionary<string, string> references)
    {
        var boundary = $"{prefix.TrimEnd('/')}/";
        var files = new List<string>();
        foreach (var file in reader.GetFiles())
        {
            if (file.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
            {
                files.Add(file);
            }
        }

        AddPackageFiles(packagesPath, reader, files, references);
    }

    /// <summary>Framework assemblies and the package contracts they supply.</summary>
    /// <param name="References">Available framework reference assemblies.</param>
    /// <param name="Overrides">Published package replacement versions.</param>
    private readonly record struct FrameworkAssetSet(Dictionary<string, string> References, Dictionary<string, NuGetVersion> Overrides);
}
