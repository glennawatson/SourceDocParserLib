// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using NuGet.Frameworks;
using NuGet.Packaging;
using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.NuGet.Models;

namespace SourceDocParser.NuGet.Tests;

/// <summary>Verifies target-specific explicit references and SDK targeting-pack selection.</summary>
public sealed class PackageReferenceAssetsTests
{
    /// <summary>The core targeting-pack fixture.</summary>
    private const string CorePack = "Microsoft.NETCore.App.Ref";

    /// <summary>The modern framework fixture.</summary>
    private const string Net10 = "net10.0";

    /// <summary>The reference directory in the modern targeting-pack fixture.</summary>
    private const string Net10References = "ref/net10.0";

    /// <summary>The .NET Framework fixture.</summary>
    private const string Net481 = "net481";

    /// <summary>The .NET Framework reference package version.</summary>
    private const string FrameworkPackVersion = "1.0.3";

    /// <summary>The synthetic package version used by reference fixtures.</summary>
    private const string PackageVersion = "1.0.0";

    /// <summary>The CLR header offset and width of the metadata directory.</summary>
    private const int MetadataDirectorySize = 8;

    /// <summary>Framework declarations select the nearest compatible scope for each package.</summary>
    /// <param name="tfm">Documentation framework.</param>
    /// <param name="expected">Expected explicit reference version.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(Net10, "10.0.0")]
    [Arguments("net10.0-windows10.0.19041.0", "10.0.0")]
    [Arguments("net9.0", "9.0.0")]
    public async Task SelectReferencesKeepsNearestFramework(string tfm, string expected)
    {
        ReferencePackage[] references =
        [
            new(CorePack, "8.0.0", "net8.0", "ref/net8.0"),
            new(CorePack, "9.0.0", "net9.0", "ref/net9.0"),
            new(CorePack, "10.0.0", Net10, Net10References),
        ];

        var result = PackageReferenceAssets.SelectReferences(references, NuGetFramework.ParseFolder(tfm));

        await Assert.That(result.Length).IsEqualTo(1);
        await Assert.That(result[0].Version).IsEqualTo(expected);
    }

    /// <summary>Package-specific scopes remain independent and incompatible frameworks are omitted.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task SelectReferencesRetainsUnscopedPackagesAndExcludesIncompatibleTargets()
    {
        ReferencePackage[] references =
        [
            new("Shared", PackageVersion, string.Empty, "lib/netstandard2.0"),
            new("Platform", "2.0.0", "net10.0-android36.0", Net10References),
            new("Microsoft.NETFramework.ReferenceAssemblies.net481", FrameworkPackVersion, Net481, "build/.NETFramework/v4.8.1"),
        ];

        var result = PackageReferenceAssets.SelectReferences(references, NuGetFramework.ParseFolder(Net10));

        await Assert.That(result.Length).IsEqualTo(1);
        await Assert.That(result[0].Id).IsEqualTo("Shared");
    }

    /// <summary>Framework targeting-pack package identifiers do not cause older framework packs to be mixed in.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task SelectReferencesTreatsFrameworkPacksAsOneFamily()
    {
        ReferencePackage[] references =
        [
            new("Microsoft.NETFramework.ReferenceAssemblies.net462", FrameworkPackVersion, "net462", "build/.NETFramework/v4.6.2"),
            new("Microsoft.NETFramework.ReferenceAssemblies.net481", FrameworkPackVersion, Net481, "build/.NETFramework/v4.8.1"),
        ];

        var result = PackageReferenceAssets.SelectReferences(references, NuGetFramework.ParseFolder(Net481));

        await Assert.That(result.Length).IsEqualTo(1);
        await Assert.That(result[0].TargetTfm).IsEqualTo(Net481);
    }

    /// <summary>An older explicit BCL targeting pack cannot replace a newer target framework.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task SelectReferencesDoesNotApplyOlderFrameworkPackToNewerTarget()
    {
        ReferencePackage[] references = [new(CorePack, "10.0.0", Net10, Net10References)];

        var result = PackageReferenceAssets.SelectReferences(references, NuGetFramework.ParseFolder("net11.0"));

        await Assert.That(result).IsEmpty();
    }

    /// <summary>A newer installed framework does not hide reference packs for the requested framework.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task FindInstalledReferenceDirectoriesMatchesFrameworkBeforeVersion()
    {
        using var directory = new ScratchDirectory();
        var expected = Directory.CreateDirectory(Path.Combine(directory.Path, CorePack, "10.0.12", "ref", Net10)).FullName;
        _ = Directory.CreateDirectory(Path.Combine(directory.Path, CorePack, "10.0.11", "ref", Net10));
        _ = Directory.CreateDirectory(Path.Combine(directory.Path, CorePack, "11.0.0", "ref", "net11.0"));
        _ = Directory.CreateDirectory(Path.Combine(directory.Path, "Microsoft.macOS.Ref.net10.0_26.0", "26.0.1", "ref", Net10));

        var result = PackageReferenceAssets.FindInstalledReferenceDirectories([directory.Path], NuGetFramework.ParseFolder(Net10));

        await Assert.That(result).IsEquivalentTo([expected]);
    }

    /// <summary>Platform workloads supply only the references for the documented platform version.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task FindInstalledReferenceDirectoriesIsolatesPlatformPacks()
    {
        using var directory = new ScratchDirectory();
        var expected = Directory.CreateDirectory(Path.Combine(directory.Path, "Microsoft.Android.Ref.36", "36.1.53", "ref", Net10)).FullName;
        _ = Directory.CreateDirectory(Path.Combine(directory.Path, "Microsoft.Android.Ref.37", "37.0.0", "ref", Net10));
        _ = Directory.CreateDirectory(Path.Combine(directory.Path, "Microsoft.macOS.Ref.net10.0_26.0", "26.0.1", "ref", Net10));

        var result = PackageReferenceAssets.FindInstalledReferenceDirectories([directory.Path], NuGetFramework.ParseFolder("net10.0-android36.0"));

        await Assert.That(result).IsEquivalentTo([expected]);
    }

    /// <summary>Declared framework references use exact SDK targeting-pack metadata.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task ReadSdkFrameworkPacksUsesDeclaredNamesAndTargetFramework()
    {
        using var directory = new ScratchDirectory();
        var path = Path.Combine(directory.Path, "BundledVersions.props");
        await File.WriteAllTextAsync(path, """
            <Project>
              <ItemGroup>
                <KnownFrameworkReference Include="Microsoft.WindowsDesktop.App.WPF" TargetFramework="net10.0" TargetingPackName="Microsoft.WindowsDesktop.App.Ref" TargetingPackVersion="10.0.12" />
                <KnownFrameworkReference Include="Microsoft.WindowsDesktop.App.WPF" TargetFramework="net11.0"
                  TargetingPackName="Microsoft.WindowsDesktop.App.Ref" TargetingPackVersion="11.0.0-preview.1" />
                <KnownFrameworkReference Include="Microsoft.AspNetCore.App" TargetFramework="net10.0" TargetingPackName="Microsoft.AspNetCore.App.Ref" TargetingPackVersion="10.0.12" />
              </ItemGroup>
            </Project>
            """);

        var result = PackageReferenceAssets.ReadSdkFrameworkPacks(path, NuGetFramework.ParseFolder("net10.0-windows10.0.19041.0"), ["Microsoft.WindowsDesktop.App.WPF"]);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result["Microsoft.WindowsDesktop.App.Ref"]).IsEqualTo("10.0.12");
    }

    /// <summary>Native framework payloads do not become Roslyn assembly references.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task AddPackageFilesExcludesDllsWithoutManagedMetadata()
    {
        using var directory = new ScratchDirectory();
        var installation = Directory.CreateDirectory(Path.Combine(directory.Path, "framework.references", PackageVersion)).FullName;
        await File.WriteAllTextAsync(Path.Combine(installation, "framework.references.nuspec"), """
            <package><metadata><id>framework.references</id><version>1.0.0</version><authors>Fixture</authors><description>Framework references.</description></metadata></package>
            """);
        var managed = Path.Combine(installation, "Managed.dll");
        File.Copy(typeof(PackageReferenceAssetsTests).Assembly.Location, managed);
        var native = await File.ReadAllBytesAsync(managed);
        await using (var stream = new MemoryStream(native))
        {
            using var image = new PEReader(stream);
            native.AsSpan(image.PEHeaders.CorHeaderStartOffset + MetadataDirectorySize, MetadataDirectorySize).Clear();
        }

        await File.WriteAllBytesAsync(Path.Combine(installation, "Native.dll"), native);
        using var reader = new PackageFolderReader(installation);
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        PackageReferenceAssets.AddPackageFiles(directory.Path, reader, ["Managed.dll", "Native.dll"], references);

        await Assert.That(references.Count).IsEqualTo(1);
        await Assert.That(references["Managed"]).IsEqualTo(managed);
    }

    /// <summary>Reference-only targeting packs supply ref assets ahead of runtime lib assets.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task AddCompatiblePackReferencesUsesRefFolder()
    {
        using var directory = new ScratchDirectory();
        var installation = Directory.CreateDirectory(Path.Combine(directory.Path, "platform.references", PackageVersion)).FullName;
        await File.WriteAllTextAsync(Path.Combine(installation, "platform.references.nuspec"), """
            <package><metadata><id>platform.references</id><version>1.0.0</version><authors>Fixture</authors><description>Platform references.</description></metadata></package>
            """);
        var referenceDirectory = Directory.CreateDirectory(Path.Combine(installation, "ref", Net10)).FullName;
        var runtimeDirectory = Directory.CreateDirectory(Path.Combine(installation, "lib", Net10)).FullName;
        var reference = Path.Combine(referenceDirectory, "Platform.dll");
        File.Copy(typeof(PackageReferenceAssetsTests).Assembly.Location, reference);
        File.Copy(typeof(PackageReferenceAssetsTests).Assembly.Location, Path.Combine(runtimeDirectory, "Platform.dll"));
        using var reader = new PackageFolderReader(installation);
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        PackageReferenceAssets.AddCompatiblePackReferences(directory.Path, reader, NuGetFramework.ParseFolder("net10.0-ios26.0"), references);

        await Assert.That(references.Count).IsEqualTo(1);
        await Assert.That(references["Platform"]).IsEqualTo(reference);
    }

    /// <summary>Owns temporary targeting-pack fixtures.</summary>
    private sealed class ScratchDirectory : IDisposable
    {
        /// <summary>Initializes a new instance of the <see cref="ScratchDirectory"/> class.</summary>
        public ScratchDirectory() => Directory.CreateDirectory(Path);

        /// <summary>Gets the temporary fixture root.</summary>
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose() => Directory.Delete(Path, true);
    }
}
