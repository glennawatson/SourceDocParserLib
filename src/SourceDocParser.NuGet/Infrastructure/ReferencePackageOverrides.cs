// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NuGet.Packaging;
using NuGet.ProjectModel;
using NuGet.Versioning;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>Selects compatible framework references according to published package policy and assembly identities.</summary>
internal static class ReferencePackageOverrides
{
    /// <summary>NuGet targeting packs publish inbox package versions in this file.</summary>
    private const string OverridesFile = "data/PackageOverrides.txt";

    /// <summary>Assembly flags that distinguish public-key and content identities.</summary>
    private const AssemblyFlags IdentityFlags = AssemblyFlags.PublicKey | AssemblyFlags.ContentTypeMask;

    /// <summary>Reads package versions supplied by a framework reference pack.</summary>
    /// <param name="reader">The downloaded reference pack.</param>
    /// <param name="overrides">Replacement versions to extend.</param>
    internal static void Read(PackageReaderBase reader, Dictionary<string, NuGetVersion> overrides)
    {
        foreach (var file in reader.GetFiles("data"))
        {
            if (!file.Equals(OverridesFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = reader.GetStream(file);
            using var lines = new StreamReader(stream);
            while (lines.ReadLine() is { } line)
            {
                var separator = line.IndexOf('|', StringComparison.Ordinal);
                if (separator <= 0 || !NuGetVersion.TryParse(line[(separator + 1)..].Trim(), out var version))
                {
                    continue;
                }

                var id = line[..separator].Trim();
                if (!overrides.TryGetValue(id, out var existing) || version > existing)
                {
                    overrides[id] = version;
                }
            }
        }
    }

    /// <summary>Selects framework assets for replaced packages while retaining the documentation root.</summary>
    /// <param name="target">The resolved package target.</param>
    /// <param name="rootId">The package whose APIs are documented.</param>
    /// <param name="frameworkReferences">Available framework reference assemblies.</param>
    /// <param name="overrides">Published framework replacement versions.</param>
    /// <param name="references">References supplied to API parsing.</param>
    internal static void Apply(
        LockFileTarget target,
        string rootId,
        Dictionary<string, string> frameworkReferences,
        Dictionary<string, NuGetVersion> overrides,
        Dictionary<string, string> references)
    {
        for (var i = 0; i < target.Libraries.Count; i++)
        {
            var library = target.Libraries[i];
            if (library is not { Name: { } id, Version: { } version }
                || id.Equals(rootId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var hasPublishedOverride = overrides.TryGetValue(id, out var supplied);
            if (supplied is not null && version > supplied)
            {
                continue;
            }

            ApplyLibraryReferences(library, hasPublishedOverride, frameworkReferences, references);
        }
    }

    /// <summary>Selects framework contracts for one eligible package.</summary>
    /// <param name="library">The selected package and its compile assets.</param>
    /// <param name="hasPublishedOverride">Whether published package policy authorizes replacement.</param>
    /// <param name="frameworkReferences">Available framework assemblies.</param>
    /// <param name="references">References supplied to API parsing.</param>
    private static void ApplyLibraryReferences(
        LockFileTargetLibrary library,
        bool hasPublishedOverride,
        Dictionary<string, string> frameworkReferences,
        Dictionary<string, string> references)
    {
        for (var a = 0; a < library.CompileTimeAssemblies.Count; a++)
        {
            var name = Path.GetFileNameWithoutExtension(library.CompileTimeAssemblies[a].Path);
            if (!frameworkReferences.TryGetValue(name, out var framework))
            {
                continue;
            }

            if (hasPublishedOverride || (references.TryGetValue(name, out var package) && IsNewerCompatibleAssembly(framework, package)))
            {
                references[name] = framework;
            }
        }
    }

    /// <summary>Checks whether a framework assembly upgrades the selected package's assembly identity.</summary>
    /// <param name="frameworkPath">The framework reference assembly.</param>
    /// <param name="packagePath">The selected package assembly.</param>
    /// <returns>Whether the framework has a matching identity and a higher assembly version.</returns>
    private static bool IsNewerCompatibleAssembly(string frameworkPath, string packagePath)
    {
        if (frameworkPath.Equals(packagePath, StringComparison.Ordinal))
        {
            return false;
        }

        using var frameworkStream = File.OpenRead(frameworkPath);
        using var packageStream = File.OpenRead(packagePath);
        try
        {
            using var framework = new PEReader(frameworkStream);
            using var package = new PEReader(packageStream);
            return framework.HasMetadata && package.HasMetadata
                && IsNewerCompatibleAssembly(framework.GetMetadataReader(), package.GetMetadataReader());
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    /// <summary>Compares assembly identities without loading either assembly.</summary>
    /// <param name="framework">The framework assembly metadata.</param>
    /// <param name="package">The selected package assembly metadata.</param>
    /// <returns>Whether the framework supplies a newer version of the same assembly identity.</returns>
    private static bool IsNewerCompatibleAssembly(MetadataReader framework, MetadataReader package)
    {
        if (!framework.IsAssembly || !package.IsAssembly)
        {
            return false;
        }

        var candidate = framework.GetAssemblyDefinition();
        var selected = package.GetAssemblyDefinition();
        return candidate.Version > selected.Version
            && (candidate.Flags & IdentityFlags) == (selected.Flags & IdentityFlags)
            && framework.GetString(candidate.Name).Equals(package.GetString(selected.Name), StringComparison.OrdinalIgnoreCase)
            && framework.GetString(candidate.Culture).Equals(package.GetString(selected.Culture), StringComparison.OrdinalIgnoreCase)
            && framework.GetBlobBytes(candidate.PublicKey).AsSpan().SequenceEqual(package.GetBlobBytes(selected.PublicKey));
    }
}
