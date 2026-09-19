// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.ProjectModel;
using SourceDocParser.LibCompilation;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>Completes required assembly references from compatible assets in the restored package graph.</summary>
internal static class PackageMetadataReferences
{
    /// <summary>The library kind representing a resolved NuGet package.</summary>
    private const string PackageKind = "package";

    /// <summary>The compile-contract asset directory.</summary>
    private const string ReferenceFolder = "ref";

    /// <summary>The framework-scoped manual reference convention used by packaged projections.</summary>
    private const string ManualLibraryFolder = "lib_manual";

    /// <summary>Typical number of framework variants in a manual reference directory.</summary>
    private const int ManualFrameworkCapacity = 4;

    /// <summary>Adds reference-only assemblies required by the documentation root.</summary>
    /// <param name="assets">The resolved NuGet graph.</param>
    /// <param name="rootId">The documentation root package identifier.</param>
    /// <param name="framework">The selected documentation framework.</param>
    /// <param name="runtimeIdentifier">The selected runtime identifier, or null for a portable graph.</param>
    /// <param name="references">Authoritative compile references, extended with required compatible package assets.</param>
    /// <exception cref="InvalidOperationException">The selected documentation target is absent.</exception>
    internal static void Add(LockFile assets, string rootId, NuGetFramework framework, string? runtimeIdentifier, Dictionary<string, string> references)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootId);
        ArgumentNullException.ThrowIfNull(framework);
        ArgumentNullException.ThrowIfNull(references);
        var target = assets.GetTarget(framework, runtimeIdentifier ?? string.Empty)
            ?? throw new InvalidOperationException($"NuGet assets have no target '{framework}/{runtimeIdentifier}' for '{rootId}'.");
        Dictionary<string, string> selected = [with(references.Count, StringComparer.OrdinalIgnoreCase)];
        foreach (var reference in references.Values)
        {
            _ = selected.TryAdd(Path.GetFileNameWithoutExtension(reference), reference);
        }

        var pending = GetRootAssemblies(assets, target, rootId);
        CompleteReferences(assets, target, GetSelectionFramework(assets, framework), pending, selected, references);
    }

    /// <summary>Completes only references required by the documented API signatures.</summary>
    /// <param name="assets">The restored package graph.</param>
    /// <param name="target">The selected framework and runtime graph.</param>
    /// <param name="framework">Framework compatibility including declared imports.</param>
    /// <param name="pending">Assemblies requiring metadata inspection.</param>
    /// <param name="selected">The selected assets by assembly name.</param>
    /// <param name="references">The parser's reference set.</param>
    private static void CompleteReferences(
        LockFile assets,
        LockFileTarget target,
        NuGetFramework framework,
        Stack<string> pending,
        Dictionary<string, string> selected,
        Dictionary<string, string> references)
    {
        Dictionary<string, string>? candidates = null;
        using var loader = new CompilationLoader(null, false) { UseOnlySuppliedReferences = true };
        while (pending.TryPop(out var path))
        {
            var loaded = loader.Load(path, references);
            var required = ApiReferenceInspector.GetMissingReferences(loaded.Assembly, false);
            var added = false;
            for (var i = 0; i < required.Length; i++)
            {
                var name = required[i].AssemblyIdentity.Name;
                if (selected.ContainsKey(name))
                {
                    continue;
                }

                candidates ??= BuildCandidates(assets, target, framework);
                if (!candidates.TryGetValue(name, out var resolved))
                {
                    continue;
                }

                selected.Add(name, resolved);
                added |= references.TryAdd(name, resolved);
            }

            if (added)
            {
                pending.Push(path);
            }
        }
    }

    /// <summary>Locates only the root package's selected compile assemblies.</summary>
    /// <param name="assets">The restored graph.</param>
    /// <param name="target">The selected target.</param>
    /// <param name="rootId">The documentation root package.</param>
    /// <returns>The assemblies from which reference traversal starts.</returns>
    private static Stack<string> GetRootAssemblies(LockFile assets, LockFileTarget target, string rootId)
    {
        var result = new Stack<string>();
        for (var i = 0; i < target.Libraries.Count; i++)
        {
            var library = target.Libraries[i];
            if (!string.Equals(library.Name, rootId, StringComparison.OrdinalIgnoreCase) || FindPackageDirectory(assets, library) is not { } directory)
            {
                continue;
            }

            for (var a = 0; a < library.CompileTimeAssemblies.Count; a++)
            {
                var asset = library.CompileTimeAssemblies[a].Path;
                if (asset.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    result.Push(Path.GetFullPath(Path.Combine(directory, asset)));
                }
            }
        }

        return result;
    }

    /// <summary>Uses only the compatibility imports recorded for the selected target.</summary>
    /// <param name="assets">The restore model containing target imports.</param>
    /// <param name="framework">The original documentation framework.</param>
    /// <returns>The framework used for package asset compatibility.</returns>
    private static NuGetFramework GetSelectionFramework(LockFile assets, NuGetFramework framework)
    {
        if (assets.PackageSpec is not { } spec)
        {
            return framework;
        }

        for (var i = 0; i < spec.TargetFrameworks.Count; i++)
        {
            var target = spec.TargetFrameworks[i];
            if (!target.FrameworkName.GetShortFolderName().Equals(framework.GetShortFolderName(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (target.FrameworkName is AssetTargetFallbackFramework or FallbackFramework)
            {
                return target.FrameworkName;
            }

            if (target.Imports.IsDefaultOrEmpty)
            {
                return framework;
            }

            return target.AssetTargetFallback
                ? new AssetTargetFallbackFramework(framework, target.Imports)
                : new FallbackFramework(framework, target.Imports);
        }

        return framework;
    }

    /// <summary>Finds compatible candidate assets only in packages selected by the target graph.</summary>
    /// <param name="assets">The restored graph.</param>
    /// <param name="target">The selected framework and runtime.</param>
    /// <param name="framework">The target's compatibility rules.</param>
    /// <returns>Managed candidate assemblies by metadata name.</returns>
    private static Dictionary<string, string> BuildCandidates(LockFile assets, LockFileTarget target, NuGetFramework framework)
    {
        Dictionary<string, string> candidates = [with(target.Libraries.Count, StringComparer.OrdinalIgnoreCase)];
        for (var i = 0; i < target.Libraries.Count; i++)
        {
            if (FindPackageDirectory(assets, target.Libraries[i]) is not { } directory)
            {
                continue;
            }

            using var reader = new PackageFolderReader(directory);
            FrameworkSpecificGroup[] referenceGroups = [.. reader.GetItems(ReferenceFolder)];
            FrameworkSpecificGroup[] libraryGroups = [.. reader.GetReferenceItems()];
            var manualGroups = GetManualGroups(reader);
            var referenceGroup = NuGetFrameworkUtility.GetNearest(referenceGroups, framework, static group => group.TargetFramework);
            var libraryGroup = NuGetFrameworkUtility.GetNearest(libraryGroups, framework, static group => group.TargetFramework);
            var manualGroup = NuGetFrameworkUtility.GetNearest(manualGroups, framework, static group => group.TargetFramework);
            if (referenceGroup is null && libraryGroup is null && manualGroup is null && framework is AssetTargetFallbackFramework fallback)
            {
                var fallbackFramework = fallback.AsFallbackFramework();
                referenceGroup = NuGetFrameworkUtility.GetNearest(referenceGroups, fallbackFramework, static group => group.TargetFramework);
                libraryGroup = NuGetFrameworkUtility.GetNearest(libraryGroups, fallbackFramework, static group => group.TargetFramework);
                manualGroup = NuGetFrameworkUtility.GetNearest(manualGroups, fallbackFramework, static group => group.TargetFramework);
            }

            AddCandidates(directory, referenceGroup, candidates);
            AddCandidates(directory, libraryGroup, candidates);
            AddCandidates(directory, manualGroup, candidates);
        }

        return candidates;
    }

    /// <summary>Finds only framework-scoped manual reference groups.</summary>
    /// <param name="reader">The selected package reader.</param>
    /// <returns>Manual groups with explicit target frameworks.</returns>
    private static FrameworkSpecificGroup[] GetManualGroups(PackageReaderBase reader)
    {
        var result = new List<FrameworkSpecificGroup>(ManualFrameworkCapacity);
        foreach (var group in reader.GetItems(ManualLibraryFolder))
        {
            if (group.TargetFramework.IsSpecificFramework)
            {
                result.Add(group);
            }
        }

        return [.. result];
    }

    /// <summary>Adds managed assets from one compatible reference or library group.</summary>
    /// <param name="directory">The selected package installation.</param>
    /// <param name="group">The compatible asset group, when present.</param>
    /// <param name="candidates">Candidate references; existing reference assets take precedence.</param>
    private static void AddCandidates(string directory, FrameworkSpecificGroup? group, Dictionary<string, string> candidates)
    {
        if (group is null)
        {
            return;
        }

        foreach (var asset in group.Items)
        {
            if (!asset.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = Path.GetFullPath(Path.Combine(directory, asset));
            if (ReadAssemblyName(path) is { } name)
            {
                _ = candidates.TryAdd(name, path);
            }
        }
    }

    /// <summary>Locates the exact package version recorded in the selected graph.</summary>
    /// <param name="assets">The graph and its package folders.</param>
    /// <param name="library">The selected package version.</param>
    /// <returns>The existing installation directory, or null for unavailable or non-package entries.</returns>
    private static string? FindPackageDirectory(LockFile assets, LockFileTargetLibrary library)
    {
        if (!string.Equals(library.Type, PackageKind, StringComparison.OrdinalIgnoreCase) || assets.GetLibrary(library.Name, library.Version) is not { } package)
        {
            return null;
        }

        for (var i = 0; i < assets.PackageFolders.Count; i++)
        {
            var directory = Path.GetFullPath(Path.Combine(assets.PackageFolders[i].Path, package.Path));
            if (Directory.Exists(directory))
            {
                return directory;
            }
        }

        return null;
    }

    /// <summary>Reads the identity of a managed candidate assembly.</summary>
    /// <param name="path">The candidate path.</param>
    /// <returns>The assembly name, or null for native binaries and modules.</returns>
    private static string? ReadAssemblyName(string path)
    {
        using var stream = File.OpenRead(path);
        if (!ManagedAssemblyExtractor.IsManagedAssembly(stream))
        {
            return null;
        }

        stream.Position = 0;
        using var image = new PEReader(stream);
        var metadata = image.GetMetadataReader();
        return metadata.IsAssembly ? metadata.GetString(metadata.GetAssemblyDefinition().Name) : null;
    }
}
