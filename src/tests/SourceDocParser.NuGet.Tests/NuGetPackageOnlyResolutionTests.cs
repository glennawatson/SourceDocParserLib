// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using SourceDocParser.NuGet.Infrastructure;

namespace SourceDocParser.NuGet.Tests;

/// <summary>Enforces package-only dependencies for published-package documentation.</summary>
public sealed class NuGetPackageOnlyResolutionTests
{
    /// <summary>The compiled NuGet library cannot depend on process drivers, MSBuild, or SDK discovery.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task CompiledNuGetLibraryUsesPackageOnlyDependencies()
    {
        await using var stream = File.OpenRead(typeof(NuGetAssemblySource).Assembly.Location);
        using var assembly = new PEReader(stream, PEStreamOptions.LeaveOpen);
        var metadata = assembly.GetMetadataReader();
        List<string> dependencies = [];
        foreach (var handle in metadata.AssemblyReferences)
        {
            var name = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
            if (name.StartsWith("Microsoft.Build", StringComparison.Ordinal))
            {
                dependencies.Add(name);
            }
        }

        foreach (var handle in metadata.TypeReferences)
        {
            var type = metadata.GetTypeReference(handle);
            var name = metadata.GetString(type.Name);
            var typeNamespace = metadata.GetString(type.Namespace);
            if (IsSdkOrProcessDependency(typeNamespace, name))
            {
                dependencies.Add($"{typeNamespace}.{name}");
            }
        }

        await Assert.That(dependencies).IsEmpty();
    }

    /// <summary>Identifies external-process and installed-SDK dependencies.</summary>
    /// <param name="typeNamespace">The referenced type's namespace.</param>
    /// <param name="name">The referenced type name.</param>
    /// <returns>True for a dependency outside package-only resolution.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSdkOrProcessDependency(string typeNamespace, string name) =>
        (typeNamespace is "System.Diagnostics" && name is "Process" or "ProcessStartInfo")
        || (typeNamespace is "SourceDocParser.LibCompilation" && name.StartsWith("DotNetSdkLocator", StringComparison.Ordinal));
}
