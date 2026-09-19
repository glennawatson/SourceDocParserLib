// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Buffers;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NuGet.Frameworks;
using NuGet.ProjectModel;
using SourceDocParser.Model;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>Selects metadata references from NuGet's resolved assets.</summary>
internal static class RestoredPackageAssets
{
    /// <summary>Bytes compared per read when packages contain the same assembly filename.</summary>
    private const int ComparisonBlockSize = 4096;

    /// <summary>One comparison block for each candidate assembly.</summary>
    private const int ComparisonBufferSize = ComparisonBlockSize * 2;

    /// <summary>Reads the compile assets for a single documentation root and target.</summary>
    /// <param name="assets">NuGet restore output.</param>
    /// <param name="rootId">Documentation root package.</param>
    /// <param name="framework">Selected framework.</param>
    /// <param name="runtimeIdentifier">Optional runtime identifier.</param>
    /// <param name="assetsPath">Assets path for diagnostics.</param>
    /// <returns>A group containing only root assemblies as documentation inputs.</returns>
    /// <exception cref="InvalidOperationException">The target or package is missing.</exception>
    internal static AssemblyGroup Read(LockFile assets, string rootId, NuGetFramework framework, string? runtimeIdentifier, string assetsPath)
    {
        var target = assets.GetTarget(framework, runtimeIdentifier ?? string.Empty)
            ?? throw new InvalidOperationException($"NuGet assets '{assetsPath}' has no target '{framework}/{runtimeIdentifier}'.");
        var selected = new Dictionary<string, CompileAsset>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<string>(1);
        for (var i = 0; i < target.Libraries.Count; i++)
        {
            var library = target.Libraries[i];
            if (!string.Equals(library.Type, "package", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var package = assets.GetLibrary(library.Name, library.Version)
                ?? throw new InvalidOperationException($"Package '{library.Name}/{library.Version}' is missing from '{assetsPath}'.");
            for (var a = 0; a < library.CompileTimeAssemblies.Count; a++)
            {
                var asset = library.CompileTimeAssemblies[a].Path;
                if (!asset.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var path = FindAsset(assets, package.Path, asset, assetsPath);
                var name = Path.GetFileNameWithoutExtension(path);
                AddReference(selected, name, new(path, GetAssetFramework(asset)), framework, assetsPath);
                if (string.Equals(library.Name, rootId, StringComparison.OrdinalIgnoreCase))
                {
                    roots.Add(path);
                }
            }
        }

        var references = new Dictionary<string, string>(selected.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var reference in selected)
        {
            references.Add(reference.Key, reference.Value.Path);
        }

        return new(framework.GetShortFolderName(), [.. roots], references) { UseOnlySuppliedReferences = true };
    }

    /// <summary>Registers a compile asset without silently replacing another package's assembly.</summary>
    /// <param name="references">Resolved references.</param>
    /// <param name="name">Assembly filename without extension.</param>
    /// <param name="candidate">Selected compile asset and its framework.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="assetsPath">Restore output for diagnostics.</param>
    /// <exception cref="InvalidOperationException">Different selected assets have the same assembly filename.</exception>
    private static void AddReference(Dictionary<string, CompileAsset> references, string name, CompileAsset candidate, NuGetFramework framework, string assetsPath)
    {
        if (!references.TryGetValue(name, out var existing))
        {
            references.Add(name, candidate);
            return;
        }

        if (existing.Path.Equals(candidate.Path, StringComparison.Ordinal))
        {
            return;
        }

        var nearest = FindNearestAsset(existing, candidate, framework);
        if (nearest is { } preferred)
        {
            references[name] = preferred;
            return;
        }

        if (HaveIdenticalContents(existing.Path, candidate.Path) || HaveEquivalentMetadata(existing.Path, candidate.Path))
        {
            return;
        }

        throw new InvalidOperationException($"NuGet graph '{assetsPath}' contains ambiguous compile assembly '{name}': '{existing.Path}' and '{candidate.Path}'.");
    }

    /// <summary>Chooses a compatible framework variant of the same assembly identity.</summary>
    /// <param name="existing">Previously selected compile asset.</param>
    /// <param name="candidate">Another selected compile asset.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <returns>The nearer asset, or null when framework compatibility cannot distinguish them.</returns>
    private static CompileAsset? FindNearestAsset(CompileAsset existing, CompileAsset candidate, NuGetFramework framework)
    {
        if (existing.Framework.Equals(candidate.Framework) || existing.Framework.IsUnsupported || candidate.Framework.IsUnsupported
            || !HaveSameAssemblyIdentity(existing.Path, candidate.Path))
        {
            return null;
        }

        var nearest = new FrameworkReducer().GetNearest(framework, [existing.Framework, candidate.Framework]);
        if (nearest is null)
        {
            return null;
        }

        return nearest.Equals(candidate.Framework) ? candidate : existing;
    }

    /// <summary>Reads the target framework declared by a NuGet compile-asset path.</summary>
    /// <param name="asset">Package-relative compile asset.</param>
    /// <returns>The asset framework, including unscoped legacy assets and unknown layouts.</returns>
    private static NuGetFramework GetAssetFramework(string asset)
    {
        var separator = asset.IndexOf('/');
        if (separator < 0)
        {
            return NuGetFramework.UnsupportedFramework;
        }

        var folder = asset.AsSpan(0, separator);
        if (!folder.Equals("lib", StringComparison.OrdinalIgnoreCase) && !folder.Equals("ref", StringComparison.OrdinalIgnoreCase))
        {
            return NuGetFramework.UnsupportedFramework;
        }

        var start = separator + 1;
        var end = asset.IndexOf('/', start);
        return end < 0 ? NuGetFramework.AnyFramework : NuGetFramework.ParseFolder(asset[start..end]);
    }

    /// <summary>Prevents framework preference from masking distinct assembly identities.</summary>
    /// <param name="firstPath">The first compile assembly.</param>
    /// <param name="secondPath">The other compile assembly.</param>
    /// <returns>True when names, versions, cultures, keys, and assembly flags agree.</returns>
    private static bool HaveSameAssemblyIdentity(string firstPath, string secondPath)
    {
        using var firstStream = File.OpenRead(firstPath);
        using var secondStream = File.OpenRead(secondPath);
        try
        {
            using var first = new PEReader(firstStream);
            using var second = new PEReader(secondStream);
            return first.HasMetadata && second.HasMetadata && HaveSameAssemblyIdentity(first.GetMetadataReader(), second.GetMetadataReader());
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    /// <summary>Compares managed assembly identities without loading either assembly.</summary>
    /// <param name="first">The first assembly's metadata.</param>
    /// <param name="second">The other assembly's metadata.</param>
    /// <returns>True when both metadata images define the same assembly identity.</returns>
    private static bool HaveSameAssemblyIdentity(MetadataReader first, MetadataReader second)
    {
        if (!first.IsAssembly || !second.IsAssembly)
        {
            return false;
        }

        var left = first.GetAssemblyDefinition();
        var right = second.GetAssemblyDefinition();
        return left.Version.Equals(right.Version) && left.Flags == right.Flags
            && first.GetString(left.Name).Equals(second.GetString(right.Name), StringComparison.OrdinalIgnoreCase)
            && first.GetString(left.Culture).Equals(second.GetString(right.Culture), StringComparison.OrdinalIgnoreCase)
            && first.GetBlobBytes(left.PublicKey).AsSpan().SequenceEqual(second.GetBlobBytes(right.PublicKey));
    }

    /// <summary>Identifies compile assemblies with identical metadata.</summary>
    /// <param name="firstPath">The first compile assembly.</param>
    /// <param name="secondPath">The other compile assembly.</param>
    /// <returns>True when both files contain exactly the same managed metadata.</returns>
    /// <remarks>Signing can alter PE checksums and certificates without changing the metadata used for documentation.</remarks>
    private static bool HaveEquivalentMetadata(string firstPath, string secondPath)
    {
        using var firstStream = File.OpenRead(firstPath);
        using var secondStream = File.OpenRead(secondPath);
        try
        {
            using var first = new PEReader(firstStream);
            using var second = new PEReader(secondStream);
            return first.HasMetadata && second.HasMetadata
                && first.GetMetadata().GetContent().AsSpan().SequenceEqual(second.GetMetadata().GetContent().AsSpan());
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    /// <summary>Identifies an assembly copied unchanged into more than one package.</summary>
    /// <param name="firstPath">The first compile assembly.</param>
    /// <param name="secondPath">The other compile assembly.</param>
    /// <returns>True when both files contain exactly the same bytes.</returns>
    private static bool HaveIdenticalContents(string firstPath, string secondPath)
    {
        using var first = File.OpenRead(firstPath);
        using var second = File.OpenRead(secondPath);
        if (first.Length != second.Length)
        {
            return false;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(ComparisonBufferSize);
        try
        {
            var firstBlock = buffer.AsSpan(0, ComparisonBlockSize);
            var secondBlock = buffer.AsSpan(ComparisonBlockSize, ComparisonBlockSize);
            while (true)
            {
                var firstRead = first.ReadAtLeast(firstBlock, ComparisonBlockSize, throwOnEndOfStream: false);
                var secondRead = second.ReadAtLeast(secondBlock, ComparisonBlockSize, throwOnEndOfStream: false);
                if (firstRead != secondRead || !firstBlock[..firstRead].SequenceEqual(secondBlock[..secondRead]))
                {
                    return false;
                }

                if (firstRead is 0)
                {
                    return true;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Locates an asset in the package folders recorded by NuGet.</summary>
    /// <param name="assets">NuGet restore output.</param>
    /// <param name="packagePath">Relative package directory.</param>
    /// <param name="asset">Relative compile asset.</param>
    /// <param name="assetsPath">Assets path for diagnostics.</param>
    /// <returns>The existing compile asset path.</returns>
    /// <exception cref="FileNotFoundException">A selected compile asset is absent.</exception>
    private static string FindAsset(LockFile assets, string packagePath, string asset, string assetsPath)
    {
        for (var i = 0; i < assets.PackageFolders.Count; i++)
        {
            var path = Path.GetFullPath(Path.Combine(assets.PackageFolders[i].Path, packagePath, asset));
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException($"Compile asset '{packagePath}/{asset}' selected by '{assetsPath}' is missing. Restore this documentation graph again.");
    }

    /// <summary>A selected compile asset within one documentation dependency graph.</summary>
    /// <param name="Path">The selected assembly path.</param>
    /// <param name="Framework">The framework declared by its NuGet asset folder.</param>
    private readonly record struct CompileAsset(string Path, NuGetFramework Framework);
}
