// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NuGet.Frameworks;
using NuGet.ProjectModel;
using NuGet.Versioning;
using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.NuGet.Tests;

/// <summary>Verifies duplicate metadata references without hiding conflicting assemblies.</summary>
public sealed class RestoredPackageAssetsTests
{
    /// <summary>The portable executable optional-header offset of its checksum.</summary>
    private const int ChecksumOffset = 64;

    /// <summary>The shared compile assembly filename.</summary>
    private const string AssemblyFile = "Duplicate.dll";

    /// <summary>The shared compile assembly identity.</summary>
    private const string AssemblyName = "Duplicate";

    /// <summary>The synthetic NuGet assets filename used in diagnostic assertions.</summary>
    private const string AssetsFile = "fixture.assets.json";

    /// <summary>The documentation root package identifier.</summary>
    private const string RootId = "Root";

    /// <summary>The dependency fixture's package directory.</summary>
    private const string DependencyDirectory = "dependency";

    /// <summary>The framework used by the synthetic restore target.</summary>
    private const string Framework = "net10.0";

    /// <summary>The platform framework supplied by one package.</summary>
    private const string PlatformFramework = "net10.0-android30.0";

    /// <summary>The documentation platform consuming compatible Android assets.</summary>
    private const string DocumentationPlatform = "net10.0-android34.0";

    /// <summary>The API emitted by generic fixture assemblies.</summary>
    private const string GenericApi = "public class GenericApi { }";

    /// <summary>The API emitted by platform fixture assemblies.</summary>
    private const string PlatformApi = "public class PlatformApi { }";

    /// <summary>Signing-related PE-header changes do not create a distinct documentation reference.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task IdenticalMetadataWithDifferentPeHeaderCoalesces()
    {
        using var directory = new ScratchDirectory("metadata-reference-equivalence");
        var first = Compile("public class SharedApi { }");
        byte[] second = [.. first];
        await using (var stream = new MemoryStream(first))
        using (var image = new PEReader(stream))
        {
            second[image.PEHeaders.PEHeaderStartOffset + ChecksumOffset] ^= 1;
        }

        var assets = await CreateAssetsAsync(directory.Path, first, second);

        var group = RestoredPackageAssets.Read(assets, RootId, NuGetFramework.ParseFolder(Framework), null, AssetsFile);

        await Assert.That(first.AsSpan().SequenceEqual(second)).IsFalse();
        await Assert.That(group.FallbackIndex.Count).IsEqualTo(1);
        await Assert.That(group.AssemblyPaths.Length).IsEqualTo(1);
        await Assert.That(group.FallbackIndex[AssemblyName]).IsEqualTo(group.AssemblyPaths[0]);
        await Assert.That(group.UseOnlySuppliedReferences).IsTrue();
    }

    /// <summary>Different API metadata with the same assembly identity remains an actionable conflict.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task DifferentMetadataWithSameAssemblyIdentityRemainsAmbiguous()
    {
        using var directory = new ScratchDirectory("metadata-reference-conflict");
        var assets = await CreateAssetsAsync(directory.Path, Compile("public class FirstApi { }"), Compile("public class SecondApi { }"));

        await Assert.That(() => RestoredPackageAssets.Read(assets, RootId, NuGetFramework.ParseFolder(Framework), null, AssetsFile))
            .Throws<InvalidOperationException>().WithMessageContaining("ambiguous compile assembly");
    }

    /// <summary>The nearest compatible platform asset wins independently of package enumeration order.</summary>
    /// <param name="reverse">True to enumerate the platform package before the generic package.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NearestCompatiblePlatformAssetWins(bool reverse)
    {
        using var directory = new ScratchDirectory("platform-reference-choice");
        var assets = await CreateAssetsAsync(directory.Path, Compile(GenericApi), Compile(PlatformApi), DocumentationPlatform, Framework, PlatformFramework);
        if (reverse)
        {
            var libraries = assets.Targets[0].Libraries;
            (libraries[0], libraries[1]) = (libraries[1], libraries[0]);
        }

        var group = RestoredPackageAssets.Read(assets, RootId, NuGetFramework.ParseFolder(DocumentationPlatform), null, AssetsFile);

        await Assert.That(group.FallbackIndex[AssemblyName]).IsEqualTo(Path.Combine(directory.Path, DependencyDirectory, "lib", PlatformFramework, AssemblyFile));
        await Assert.That(group.AssemblyPaths.Length).IsEqualTo(1);
        await Assert.That(group.UseOnlySuppliedReferences).IsTrue();
    }

    /// <summary>Reference selection remains isolated between neutral and platform documentation graphs.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task DifferentDocumentationFrameworksKeepIndependentReferenceChoices()
    {
        using var directory = new ScratchDirectory("framework-reference-isolation");
        var generic = Compile(GenericApi);
        var platform = Compile(PlatformApi);
        var androidAssets = await CreateAssetsAsync(directory.Path, generic, platform, DocumentationPlatform, Framework, PlatformFramework);
        var neutralAssets = await CreateAssetsAsync(directory.Path, generic, platform, Framework, Framework, PlatformFramework);

        var android = RestoredPackageAssets.Read(androidAssets, RootId, NuGetFramework.ParseFolder(DocumentationPlatform), null, "android.assets.json");
        var neutral = RestoredPackageAssets.Read(neutralAssets, RootId, NuGetFramework.ParseFolder(Framework), null, "neutral.assets.json");

        await Assert.That(android.FallbackIndex[AssemblyName]).Contains(PlatformFramework);
        await Assert.That(neutral.FallbackIndex[AssemblyName]).IsEqualTo(Path.Combine(directory.Path, "root", "lib", Framework, AssemblyFile));
    }

    /// <summary>A closer target framework cannot replace an assembly with a different identity.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task PlatformPreferenceDoesNotHideAssemblyIdentityConflicts()
    {
        using var directory = new ScratchDirectory("assembly-identity-conflict");
        var platform = Compile("[assembly: System.Reflection.AssemblyVersion(\"1.0.0.0\")] public class PlatformApi { }");
        var assets = await CreateAssetsAsync(directory.Path, Compile(GenericApi), platform, DocumentationPlatform, Framework, PlatformFramework);

        await Assert.That(() => RestoredPackageAssets.Read(assets, RootId, NuGetFramework.ParseFolder(DocumentationPlatform), null, AssetsFile))
            .Throws<InvalidOperationException>().WithMessageContaining("ambiguous compile assembly");
    }

    /// <summary>Builds a small managed assembly with a fixed assembly identity.</summary>
    /// <param name="source">Public API under test.</param>
    /// <returns>The emitted assembly bytes.</returns>
    /// <exception cref="InvalidOperationException">The fixture cannot be compiled.</exception>
    private static byte[] Compile(string source)
    {
        var compilation = CSharpCompilation.Create(
            AssemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException("Could not compile the metadata equivalence fixture.");
        }

        return stream.ToArray();
    }

    /// <summary>Creates restore inputs containing two packages that supply the same compile assembly name.</summary>
    /// <param name="directory">Fixture package directory.</param>
    /// <param name="first">The root package's assembly.</param>
    /// <param name="second">The dependency package's assembly.</param>
    /// <returns>The synthetic resolved graph.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Task<LockFile> CreateAssetsAsync(string directory, byte[] first, byte[] second) =>
        CreateAssetsAsync(directory, first, second, Framework, Framework, Framework);

    /// <summary>Creates selected compile assets for a specific documentation framework.</summary>
    /// <param name="directory">Fixture package directory.</param>
    /// <param name="first">The root package's assembly.</param>
    /// <param name="second">The dependency package's assembly.</param>
    /// <param name="targetFramework">Documentation target framework.</param>
    /// <param name="firstFramework">Framework folder of the first asset.</param>
    /// <param name="secondFramework">Framework folder of the second asset.</param>
    /// <returns>The synthetic resolved graph.</returns>
    private static async Task<LockFile> CreateAssetsAsync(string directory, byte[] first, byte[] second, string targetFramework, string firstFramework, string secondFramework)
    {
        const string packageKind = "package";
        var root = Directory.CreateDirectory(Path.Combine(directory, "root", "lib", firstFramework)).FullName;
        var dependency = Directory.CreateDirectory(Path.Combine(directory, DependencyDirectory, "lib", secondFramework)).FullName;
        await File.WriteAllBytesAsync(Path.Combine(root, AssemblyFile), first);
        await File.WriteAllBytesAsync(Path.Combine(dependency, AssemblyFile), second);
        var version = NuGetVersion.Parse("1.0.0");
        var framework = NuGetFramework.ParseFolder(targetFramework);
        return new()
        {
            PackageFolders = [new(directory)],
            Libraries =
            [
                new() { Name = RootId, Version = version, Type = packageKind, Path = "root" },
                new() { Name = "Dependency", Version = version, Type = packageKind, Path = DependencyDirectory },
            ],
            Targets =
            [
                new()
                {
                    TargetFramework = framework,
                    Libraries =
                    [
                        new() { Name = RootId, Version = version, Type = packageKind, CompileTimeAssemblies = [new($"lib/{firstFramework}/{AssemblyFile}")] },
                        new() { Name = "Dependency", Version = version, Type = packageKind, CompileTimeAssemblies = [new($"lib/{secondFramework}/{AssemblyFile}")] },
                    ],
                },
            ],
        };
    }
}
