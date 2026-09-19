// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NuGet.Frameworks;
using NuGet.Packaging;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>Supplies NuGet compatibility facades required by .NET Framework metadata.</summary>
internal static class FrameworkCompatibilityReferences
{
    /// <summary>The NuGet package carrying .NET Framework compatibility facade data.</summary>
    private const string CompatibilityPackage = "Microsoft.NET.Build.Extensions";

    /// <summary>The stable compatibility package release.</summary>
    private const string CompatibilityVersion = "2.2.101";

    /// <summary>The reference-data directory applicable to .NET Framework 4.6.1 and later.</summary>
    private const string FacadePrefix = "msbuildExtensions/Microsoft/Microsoft.NET.Build.Extensions/net461/lib/";

    /// <summary>The contract assembly required by .NET Standard 2.0 libraries.</summary>
    private const string StandardAssembly = "netstandard";

    /// <summary>The earliest .NET Framework version supporting the compatibility facade.</summary>
    private static readonly Version _minimumFramework = new(4, 6, 1);

    /// <summary>Adds compatibility references only when a selected assembly requires them.</summary>
    /// <param name="session">Configured NuGet package session.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="references">Selected assembly references.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing compatibility reference acquisition.</returns>
    internal static async Task AddAsync(PackageRestoreSession session, NuGetFramework framework, Dictionary<string, string> references, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(framework);
        ArgumentNullException.ThrowIfNull(references);
        cancellationToken.ThrowIfCancellationRequested();
        if (framework.Framework is not FrameworkConstants.FrameworkIdentifiers.Net || framework.Version < _minimumFramework
            || references.ContainsKey(StandardAssembly) || !RequiresStandardFacade(references))
        {
            return;
        }

        using var package = await session.DownloadAsync(CompatibilityPackage, CompatibilityVersion, cancellationToken).ConfigureAwait(false);
        AddRequiredFacades(session.PackagesPath, package.PackageReader!, references, cancellationToken);
    }

    /// <summary>Detects a required .NET Standard assembly in supplied metadata.</summary>
    /// <param name="references">Selected compile and framework references.</param>
    /// <returns>True when a selected managed assembly references the compatibility contract.</returns>
    private static bool RequiresStandardFacade(Dictionary<string, string> references)
    {
        foreach (var path in references.Values)
        {
            using var stream = File.OpenRead(path);
            using var image = new PEReader(stream);
            if (!image.HasMetadata)
            {
                continue;
            }

            var metadata = image.GetMetadataReader();
            foreach (var handle in metadata.AssemblyReferences)
            {
                var reference = metadata.GetAssemblyReference(handle);
                if (metadata.GetString(reference.Name).Equals(StandardAssembly, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Adds the required facade and its missing references from package data only.</summary>
    /// <param name="packagesPath">NuGet package cache directory.</param>
    /// <param name="package">Downloaded compatibility package.</param>
    /// <param name="references">Selected references, which remain authoritative.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static void AddRequiredFacades(string packagesPath, PackageReaderBase package, Dictionary<string, string> references, CancellationToken cancellationToken)
    {
        var candidates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in package.GetFiles())
        {
            if (path.StartsWith(FacadePrefix, StringComparison.OrdinalIgnoreCase) && path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                candidates[Path.GetFileNameWithoutExtension(path)] = path;
            }
        }

        var pending = new Queue<string>();
        pending.Enqueue(StandardAssembly);
        while (pending.TryDequeue(out var name))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (references.ContainsKey(name) || !candidates.TryGetValue(name, out var path))
            {
                continue;
            }

            PackageReferenceAssets.AddPackageFiles(packagesPath, package, [path], references);
            using var stream = package.GetStream(path);
            using var image = new PEReader(stream);
            var metadata = image.GetMetadataReader();
            foreach (var handle in metadata.AssemblyReferences)
            {
                var reference = metadata.GetAssemblyReference(handle);
                var dependency = metadata.GetString(reference.Name);
                if (!references.ContainsKey(dependency) && candidates.ContainsKey(dependency))
                {
                    pending.Enqueue(dependency);
                }
            }
        }
    }
}
