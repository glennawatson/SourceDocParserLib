// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Xml.Linq;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging;
using NuGet.ProjectModel;
using NuGet.Versioning;
using SourceDocParser.LibCompilation;
using SourceDocParser.NuGet.Models;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>Supplies explicit package constraints and target-framework reference assemblies.</summary>
internal static class PackageReferenceAssets
{
    /// <summary>The .NET Standard compatibility package used by SDK projects.</summary>
    private const string StandardLibrary = "NETStandard.Library";

    /// <summary>The reference package family for .NET Framework SDK projects.</summary>
    private const string FrameworkReferences = "Microsoft.NETFramework.ReferenceAssemblies";

    /// <summary>The SDK pack supplying the base .NET reference assemblies.</summary>
    private const string CoreReferencePack = "Microsoft.NETCore.App.Ref";

    /// <summary>The SDK pack supplying .NET Standard 2.1 reference assemblies.</summary>
    private const string StandardReferencePack = "NETStandard.Library.Ref";

    /// <summary>Platform pack names carry major and minor platform versions.</summary>
    private const int PlatformVersionComponents = 2;

    /// <summary>Adds applicable explicit references as package constraints or targeting-pack downloads.</summary>
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
            if (ContainsDependency(dependencies, reference.Id))
            {
                continue;
            }

            using var download = await session.DownloadAsync(reference.Id, reference.Version, cancellationToken).ConfigureAwait(false);
            var reader = download.PackageReader!;
            var identity = reader.GetIdentity();
            if (IsFrameworkPackage(reference, reader.NuspecReader))
            {
                downloads.Add(new(identity.Id, ExactVersion(identity.Version)));
                continue;
            }

            var range = reference.Version is null ? ExactVersion(identity.Version) : PackageGraphRestore.ParsePin(reference.Version);
            dependencies.Add(PackageGraphRestore.Dependency(reference.Id, range));
        }

        AppleReferencePacks.AddDownloads(session, framework, downloads);
        return downloads;
    }

    /// <summary>Adds framework references while retaining the graph's selected package assets.</summary>
    /// <param name="session">Configured package acquisition session.</param>
    /// <param name="config">Reference declarations and dependency pins.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="references">References selected by restore.</param>
    /// <param name="assets">Resolved package and framework declarations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing reference discovery.</returns>
    internal static async Task AddFrameworkAssetsAsync(
        PackageRestoreSession session,
        PackageConfig config,
        NuGetFramework framework,
        Dictionary<string, string> references,
        LockFile assets,
        CancellationToken cancellationToken)
    {
        var selected = SelectReferences(config.ReferencePackages, framework);
        var frameworkAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await AddExplicitFrameworkAssetsAsync(session, selected, assets, frameworkAssets, false, cancellationToken).ConfigureAwait(false);
        await AddDeclaredFrameworkPacksAsync(session, config, framework, assets, frameworkAssets, cancellationToken).ConfigureAwait(false);
        await AddExplicitFrameworkAssetsAsync(session, selected, assets, frameworkAssets, true, cancellationToken).ConfigureAwait(false);
        await AddSdkDownloadsAsync(session, framework, assets, frameworkAssets, cancellationToken).ConfigureAwait(false);
        var directories = FindInstalledReferenceDirectories(DotNetSdkLocator.EnumeratePackRoots(), framework);
        for (var i = 0; i < directories.Length; i++)
        {
            AddDirectory(directories[i], frameworkAssets);
        }

        await AddImplicitFrameworkAssetsAsync(session, config, framework, assets, frameworkAssets, cancellationToken).ConfigureAwait(false);
        foreach (var reference in frameworkAssets)
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

    /// <summary>Finds installed targeting packs for the exact framework or its unqualified base framework.</summary>
    /// <param name="packRoots">Installed SDK pack directories.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <returns>The matching reference directories, in SDK-root priority order.</returns>
    internal static string[] FindInstalledReferenceDirectories(IReadOnlyList<string> packRoots, NuGetFramework framework)
    {
        var result = new List<string>(packRoots.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var r = 0; r < packRoots.Count; r++)
        {
            if (!Directory.Exists(packRoots[r]))
            {
                continue;
            }

            var packs = Directory.GetDirectories(packRoots[r]);
            for (var p = 0; p < packs.Length; p++)
            {
                var name = Path.GetFileName(packs[p]);
                if (!IsApplicablePack(name, framework) || seen.Contains(name))
                {
                    continue;
                }

                var directory = FindPackDirectory(packs[p], framework);
                if (directory is null)
                {
                    continue;
                }

                result.Add(directory);
                _ = seen.Add(name);
            }
        }

        return [.. result];
    }

    /// <summary>Reads the SDK's targeting-pack identities for declared framework references.</summary>
    /// <param name="path">SDK bundled-versions props file.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="names">Declared framework reference names.</param>
    /// <returns>The SDK's targeting-pack version by package identifier.</returns>
    internal static Dictionary<string, string> ReadSdkFrameworkPacks(string path, NuGetFramework framework, HashSet<string> names)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
        {
            return result;
        }

        var baseFramework = new NuGetFramework(framework.Framework, framework.Version);
        var document = XDocument.Load(path);
        foreach (var element in document.Descendants("KnownFrameworkReference"))
        {
            if (!MatchesFrameworkReference(element, baseFramework, names))
            {
                continue;
            }

            var pack = element.Attribute("TargetingPackName")?.Value;
            var version = element.Attribute("TargetingPackVersion")?.Value;
            if (pack is not null && version is not null && NuGetVersion.TryParse(version, out _))
            {
                result[pack] = version;
            }
        }

        return result;
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

    /// <summary>Adds configured targeting packs at their framework priority.</summary>
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
        Dictionary<string, string> references,
        bool baseFrameworkOnly,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < selected.Length; i++)
        {
            var reference = selected[i];
            if (ContainsPackage(assets, reference.Id) || reference.Id.Equals(CoreReferencePack, StringComparison.OrdinalIgnoreCase) != baseFrameworkOnly)
            {
                continue;
            }

            using var download = await session.DownloadAsync(reference.Id, reference.Version, cancellationToken).ConfigureAwait(false);
            if (IsFrameworkPackage(reference, download.PackageReader!.NuspecReader))
            {
                AddPackagePrefix(session.PackagesPath, download.PackageReader, reference.PathPrefix, references);
            }
        }
    }

    /// <summary>Checks whether restore supplies an ordinary reference package.</summary>
    /// <param name="assets">Resolved dependency graph.</param>
    /// <param name="id">Reference package identifier.</param>
    /// <returns>True when the package participates in the restored graph.</returns>
    private static bool ContainsPackage(LockFile assets, string id)
    {
        for (var i = 0; i < assets.Libraries.Count; i++)
        {
            if (string.Equals(assets.Libraries[i].Name, id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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

    /// <summary>Identifies SDK reference packs applicable to a target framework.</summary>
    /// <param name="name">SDK pack name.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <returns>True when the pack can supply target framework references.</returns>
    private static bool IsApplicablePack(string name, NuGetFramework framework)
    {
        if (framework.Framework is FrameworkConstants.FrameworkIdentifiers.NetStandard)
        {
            return name.Equals(StandardReferencePack, StringComparison.OrdinalIgnoreCase);
        }

        if (framework.Framework is not FrameworkConstants.FrameworkIdentifiers.NetCoreApp)
        {
            return false;
        }

        if (name.Equals(CoreReferencePack, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!framework.HasPlatform)
        {
            return false;
        }

        if (framework.Platform.Equals("windows", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var prefix = $"Microsoft.{framework.Platform}.Ref.";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = name[prefix.Length..];
        if (framework.Platform.Equals("android", StringComparison.OrdinalIgnoreCase))
        {
            return NuGetVersion.TryParse(suffix, out var version)
                && version.Major == framework.PlatformVersion.Major
                && version.Minor == framework.PlatformVersion.Minor;
        }

        var baseFramework = new NuGetFramework(framework.Framework, framework.Version);
        return suffix.StartsWith($"{baseFramework.GetShortFolderName()}_{framework.PlatformVersion.ToString(PlatformVersionComponents)}", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Acquires targeting packs selected by the SDK for framework references in the resolved graph.</summary>
    /// <param name="session">Configured package acquisition session.</param>
    /// <param name="config">Explicit framework pins.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="assets">Resolved package and framework declarations.</param>
    /// <param name="references">Resolved assembly references.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing targeting-pack acquisition.</returns>
    private static async Task AddDeclaredFrameworkPacksAsync(
        PackageRestoreSession session,
        PackageConfig config,
        NuGetFramework framework,
        LockFile assets,
        Dictionary<string, string> references,
        CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (framework.Framework is FrameworkConstants.FrameworkIdentifiers.NetCoreApp)
        {
            _ = names.Add("Microsoft.NETCore.App");
        }

        var target = assets.GetTarget(framework, config.RuntimeIdentifier ?? string.Empty);
        if (target is not null)
        {
            for (var i = 0; i < target.Libraries.Count; i++)
            {
                names.UnionWith(target.Libraries[i].FrameworkReferences);
            }
        }

        var sdk = FindSdkDirectory(assets, framework);
        if (sdk is null)
        {
            return;
        }

        var path = Path.Combine(sdk, "Microsoft.NETCoreSdk.BundledVersions.props");
        var packs = ReadSdkFrameworkPacks(path, framework, names);
        var explicitReferences = SelectReferences(config.ReferencePackages, framework);
        var orderedPacks = OrderFrameworkPacks(packs);
        for (var i = 0; i < orderedPacks.Count; i++)
        {
            var pack = orderedPacks[i];
            if (HasExplicitPack(explicitReferences, pack.Key))
            {
                continue;
            }

            var baseTfm = new NuGetFramework(framework.Framework, framework.Version).GetShortFolderName();
            var installation = Path.GetDirectoryName(Path.GetDirectoryName(sdk))!;
            var installed = Path.Combine(installation, "packs", pack.Key, pack.Value, "ref", baseTfm);
            if (Directory.Exists(installed))
            {
                AddDirectory(installed, references);
                continue;
            }

            using var download = await session.DownloadAsync(pack.Key, pack.Value, cancellationToken).ConfigureAwait(false);
            AddPackagePrefix(session.PackagesPath, download.PackageReader!, $"ref/{new NuGetFramework(framework.Framework, framework.Version).GetShortFolderName()}", references);
        }
    }

    /// <summary>Places specialized frameworks before base-framework compatibility facades.</summary>
    /// <param name="packs">Targeting-pack identities selected by the SDK.</param>
    /// <returns>Targeting packs in assembly-conflict priority order.</returns>
    private static List<KeyValuePair<string, string>> OrderFrameworkPacks(Dictionary<string, string> packs)
    {
        var ordered = new List<KeyValuePair<string, string>>(packs.Count);
        foreach (var pack in packs)
        {
            if (!pack.Key.Equals(CoreReferencePack, StringComparison.OrdinalIgnoreCase))
            {
                ordered.Add(pack);
            }
        }

        if (packs.TryGetValue(CoreReferencePack, out var version))
        {
            ordered.Add(new(CoreReferencePack, version));
        }

        return ordered;
    }

    /// <summary>Adds the targeting-pack assets selected by SDK evaluation.</summary>
    /// <param name="session">Configured package acquisition session.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="assets">Evaluated SDK download dependencies.</param>
    /// <param name="references">Framework reference assemblies.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing SDK targeting-pack acquisition.</returns>
    private static async Task AddSdkDownloadsAsync(
        PackageRestoreSession session,
        NuGetFramework framework,
        LockFile assets,
        Dictionary<string, string> references,
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
                AddCompatiblePackReferences(session.PackagesPath, download.PackageReader!, framework, references);
            }
        }
    }

    /// <summary>Matches SDK metadata to a declared framework reference.</summary>
    /// <param name="element">SDK framework-reference metadata.</param>
    /// <param name="framework">Target's unqualified base framework.</param>
    /// <param name="names">Declared framework reference names.</param>
    /// <returns>True when the metadata supplies a declared framework.</returns>
    private static bool MatchesFrameworkReference(XElement element, NuGetFramework framework, HashSet<string> names)
    {
        var name = element.Attribute("Include")?.Value;
        var tfm = element.Attribute("TargetFramework")?.Value;
        return name is not null && names.Contains(name) && tfm is not null && framework.Equals(NuGetFramework.ParseFolder(tfm));
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

    /// <summary>Locates the SDK selected during project evaluation.</summary>
    /// <param name="assets">Evaluated SDK metadata.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <returns>The selected SDK directory, or null when restore provides no SDK metadata.</returns>
    private static string? FindSdkDirectory(LockFile assets, NuGetFramework framework)
    {
        var targets = assets.PackageSpec.TargetFrameworks;
        for (var i = 0; i < targets.Count; i++)
        {
            if (targets[i].FrameworkName.Equals(framework) && targets[i].RuntimeIdentifierGraphPath is { Length: > 0 } path)
            {
                return Path.GetDirectoryName(path);
            }
        }

        return null;
    }

    /// <summary>Chooses the installed pack version containing the requested reference framework.</summary>
    /// <param name="pack">SDK pack directory.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <returns>The matching reference directory, or null when unavailable.</returns>
    private static string? FindPackDirectory(string pack, NuGetFramework framework)
    {
        var versions = Directory.GetDirectories(pack);
        var baseFramework = new NuGetFramework(framework.Framework, framework.Version);
        string? selected = null;
        NuGetVersion? selectedVersion = null;
        for (var v = 0; v < versions.Length; v++)
        {
            if (!NuGetVersion.TryParse(Path.GetFileName(versions[v]), out var version))
            {
                continue;
            }

            var reference = Path.Combine(versions[v], "ref");
            if (!Directory.Exists(reference))
            {
                continue;
            }

            var directories = Directory.GetDirectories(reference);
            for (var d = 0; d < directories.Length; d++)
            {
                var candidate = NuGetFramework.ParseFolder(Path.GetFileName(directories[d]));
                if (!candidate.Equals(framework) && !candidate.Equals(baseFramework))
                {
                    continue;
                }

                if (selectedVersion is not null && selectedVersion >= version)
                {
                    continue;
                }

                selected = directories[d];
                selectedVersion = version;
            }
        }

        return selected;
    }

    /// <summary>Supplies reference assemblies carried by implicit SDK package dependencies.</summary>
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
        Dictionary<string, string> references,
        CancellationToken cancellationToken)
    {
        if (framework.Framework is FrameworkConstants.FrameworkIdentifiers.NetStandard && framework.Version < new Version(2, 1))
        {
            var version = FindResolvedVersion(assets, framework, config.RuntimeIdentifier, StandardLibrary)
                ?? throw new InvalidOperationException($"NuGet target '{framework}' does not contain required framework reference package '{StandardLibrary}'.");
            using var download = await session.DownloadAsync(StandardLibrary, version, cancellationToken).ConfigureAwait(false);
            AddPackagePrefix(session.PackagesPath, download.PackageReader!, "build/netstandard2.0/ref", references);
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
            AddPackagePrefix(session.PackagesPath, download.PackageReader!, "build/.NETFramework", references);
        }
    }

    /// <summary>Reads an implicit framework package's version from the resolved target.</summary>
    /// <param name="assets">Resolved package versions.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="runtimeIdentifier">Optional runtime target.</param>
    /// <param name="id">Framework package identifier.</param>
    /// <returns>The exact package version, or null when the framework is supplied by the SDK installation.</returns>
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

    /// <summary>Adds an installed framework's assembly references without replacing package assets.</summary>
    /// <param name="directory">Framework reference directory.</param>
    /// <param name="references">Resolved assembly references.</param>
    private static void AddDirectory(string directory, Dictionary<string, string> references)
    {
        var files = Directory.GetFiles(directory, "*.dll");
        for (var i = 0; i < files.Length; i++)
        {
            _ = references.TryAdd(Path.GetFileNameWithoutExtension(files[i]), files[i]);
        }
    }
}
