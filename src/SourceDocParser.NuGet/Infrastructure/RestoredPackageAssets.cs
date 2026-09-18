// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Buffers;
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
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                AddReference(references, name, path, assetsPath);
                if (string.Equals(library.Name, rootId, StringComparison.OrdinalIgnoreCase))
                {
                    roots.Add(path);
                }
            }
        }

        return new(framework.GetShortFolderName(), [.. roots], references);
    }

    /// <summary>Registers a compile asset without silently replacing another package's assembly.</summary>
    /// <param name="references">Resolved references.</param>
    /// <param name="name">Assembly filename without extension.</param>
    /// <param name="path">Selected compile asset.</param>
    /// <param name="assetsPath">Restore output for diagnostics.</param>
    /// <exception cref="InvalidOperationException">Different selected assets have the same assembly filename.</exception>
    private static void AddReference(Dictionary<string, string> references, string name, string path, string assetsPath)
    {
        if (!references.TryGetValue(name, out var existing))
        {
            references.Add(name, path);
            return;
        }

        if (existing.Equals(path, StringComparison.Ordinal) || HaveIdenticalContents(existing, path))
        {
            return;
        }

        throw new InvalidOperationException($"NuGet graph '{assetsPath}' contains ambiguous compile assembly '{name}': '{existing}' and '{path}'.");
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
}
