// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NuGet.Frameworks;
using NuGet.ProjectModel;
using NuGet.Versioning;
using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.NuGet.Tests;

/// <summary>Verifies metadata-only completion from already restored package assets.</summary>
public sealed class PackageMetadataReferencesTests
{
    /// <summary>The documentation root package and assembly.</summary>
    private const string Root = "DocRoot";

    /// <summary>The dependency assembly required by the root.</summary>
    private const string Needed = "Needed";

    /// <summary>The common framework used by package fixtures.</summary>
    private const string Net10 = "net10.0";

    /// <summary>The Windows framework used by fallback fixtures.</summary>
    private const string Windows = "net10.0-windows10.0.19041.0";

    /// <summary>The desktop asset group exposed through declared imports.</summary>
    private const string Net462 = "net462";

    /// <summary>The package whose assemblies remain reference-only.</summary>
    private const string Browser = "Browser";

    /// <summary>The compatible desktop library asset.</summary>
    private const string DesktopAsset = "lib/net462/Needed.dll";

    /// <summary>The framework-scoped projection supplied outside automatic compile assets.</summary>
    private const string ManualAsset = "lib_manual/net8.0-windows10.0.17763.0/Needed.dll";

    /// <summary>The dependency API referenced by the documentation root.</summary>
    private const string NeededSource = "public class NeededApi { }";

    /// <summary>The documentation root's dependency-bearing API.</summary>
    private const string RootSource = "public class DocRootApi { public NeededApi Value { get; set; } }";

    /// <summary>Only inherited API members require the referenced type's dependency surface.</summary>
    /// <param name="inheritsDependency">Whether the root inherits the dependency's public members.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AddIncludesOnlyRequiredLibraryClosure(bool inheritsDependency)
    {
        using var fixture = new Fixture("net481");
        var core = Compile("BrowserCore", "public class BrowserCoreApi { }");
        var needed = Compile(Needed, "public class NeededApi { public BrowserCoreApi Core { get; set; } }", core);
        var unused = Compile("BrowserUnused", "public class BrowserUnusedApi { }");
        fixture.AddPackage(
            Browser,
            [],
            (DesktopAsset, needed),
            ("lib/net462/BrowserCore.dll", core),
            ("lib/net462/BrowserUnused.dll", unused));
        var source = inheritsDependency ? "public class DocRootApi : NeededApi { }" : RootSource;
        fixture.AddRoot(Compile(Root, source, needed, core));

        fixture.Complete();

        await Assert.That(fixture.References.ContainsKey(Needed)).IsTrue();
        await Assert.That(fixture.References.ContainsKey("BrowserCore")).IsEqualTo(inheritsDependency);
        await Assert.That(fixture.References.ContainsKey("BrowserUnused")).IsFalse();
        await Assert.That(fixture.Target.Libraries[1].CompileTimeAssemblies.Count).IsEqualTo(1);
    }

    /// <summary>Private implementation dependencies do not trigger supplemental package inspection.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task AddDoesNotTraversePrivatePackageReferences()
    {
        using var fixture = new Fixture(Net10);
        var needed = Compile(Needed, NeededSource);
        fixture.AddPackage(Browser, [], ("lib/net10.0/Needed.dll", needed));
        fixture.AddRoot(Compile(Root, "public class DocRootApi { private NeededApi Value; }", needed));

        fixture.Complete();

        await Assert.That(fixture.References.ContainsKey(Needed)).IsFalse();
    }

    /// <summary>Reference assets win over compatible lib implementations.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task AddPrefersReferenceAssets()
    {
        using var fixture = new Fixture(Net10);
        var needed = Compile(Needed, NeededSource);
        fixture.AddPackage("Dependency", [], ("ref/netstandard2.0/Needed.dll", needed), ("lib/net10.0/Needed.dll", needed));
        fixture.AddRoot(Compile(Root, RootSource, needed));

        fixture.Complete();

        await Assert.That(fixture.References[Needed].Replace(Path.DirectorySeparatorChar, '/')).EndsWith("/ref/netstandard2.0/Needed.dll");
    }

    /// <summary>Declared NuGet asset fallbacks permit compatible desktop assets without changing the root framework.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task AddHonorsDeclaredAssetTargetFallback()
    {
        using var fixture = new Fixture(Windows);
        var needed = Compile(Needed, NeededSource);
        fixture.AddPackage(Browser, [], (DesktopAsset, needed));
        fixture.AddRoot(Compile(Root, RootSource, needed));
        var fallback = NuGetFramework.ParseFolder(Net462);
        fixture.Assets.PackageSpec = new([new TargetFrameworkInformation
        {
            FrameworkName = new AssetTargetFallbackFramework(fixture.Framework, [fallback]),
            Imports = [fallback],
            AssetTargetFallback = true,
        }]);

        fixture.Complete();

        await Assert.That(fixture.References[Needed].Replace(Path.DirectorySeparatorChar, '/')).EndsWith("/lib/net462/Needed.dll");
        await Assert.That(fixture.Target.TargetFramework).IsSameReferenceAs(fixture.Framework);
    }

    /// <summary>Desktop framework compatibility is not invented for a modern Windows target.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task AddDoesNotInventFrameworkFallback()
    {
        using var fixture = new Fixture(Windows);
        var needed = Compile(Needed, NeededSource);
        fixture.AddPackage(Browser, [], (DesktopAsset, needed));
        fixture.AddRoot(Compile(Root, RootSource, needed));

        fixture.Complete();

        await Assert.That(fixture.References.ContainsKey(Needed)).IsFalse();
    }

    /// <summary>Required manual projections use the nearest compatible framework without adding unused sibling assemblies.</summary>
    /// <param name="tfm">The documentation framework.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments("net9.0-windows10.0.19041.0")]
    [Arguments(Windows)]
    public async Task AddFindsFrameworkScopedManualProjections(string tfm)
    {
        using var fixture = new Fixture(tfm);
        var needed = Compile(Needed, NeededSource);
        var unused = Compile("Unused", "public class UnusedApi { }");
        fixture.AddPackage(
            Browser,
            [],
            (ManualAsset, needed),
            ("lib_manual/net6.0-windows10.0.17763.0/Needed.dll", needed),
            ("lib_manual/net8.0-windows10.0.17763.0/Unused.dll", unused));
        fixture.AddRoot(Compile(Root, RootSource, needed));

        fixture.Complete();

        await Assert.That(fixture.References[Needed].Replace(Path.DirectorySeparatorChar, '/')).EndsWith($"/{ManualAsset}");
        await Assert.That(fixture.References.ContainsKey("Unused")).IsFalse();
    }

    /// <summary>Manual projections for a different platform or newer framework cannot satisfy a reference.</summary>
    /// <param name="tfm">The incompatible documentation framework.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(Net10)]
    [Arguments("net10.0-android36.0")]
    [Arguments("net7.0-windows10.0.19041.0")]
    public async Task AddExcludesIncompatibleManualProjections(string tfm)
    {
        using var fixture = new Fixture(tfm);
        var needed = Compile(Needed, NeededSource);
        fixture.AddPackage(Browser, [], (ManualAsset, needed));
        fixture.AddRoot(Compile(Root, RootSource, needed));

        fixture.Complete();

        await Assert.That(fixture.References.ContainsKey(Needed)).IsFalse();
    }

    /// <summary>Native payloads in a compatible manual group cannot enter the metadata reference set.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task AddExcludesNativeManualPayloads()
    {
        using var fixture = new Fixture(Windows);
        var needed = Compile(Needed, NeededSource);
        fixture.AddPackage(Browser, [], (ManualAsset, [0x4D, 0x5A]));
        fixture.AddRoot(Compile(Root, RootSource, needed));

        fixture.Complete();

        await Assert.That(fixture.References.ContainsKey(Needed)).IsFalse();
    }

    /// <summary>Unscoped manual, runtime, tool, and analyzer directories are not compile-reference conventions.</summary>
    /// <param name="path">The unsupported asset location.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments("lib_manual/Needed.dll")]
    [Arguments("manual/net8.0-windows10.0.17763.0/Needed.dll")]
    [Arguments("runtimes/win-x64/lib/net8.0/Needed.dll")]
    [Arguments("tools/net8.0/Needed.dll")]
    [Arguments("analyzers/dotnet/cs/Needed.dll")]
    public async Task AddExcludesNonReferenceLocations(string path)
    {
        using var fixture = new Fixture(Windows);
        var needed = Compile(Needed, NeededSource);
        fixture.AddPackage(Browser, [], (path, needed));
        fixture.AddRoot(Compile(Root, RootSource, needed));

        fixture.Complete();

        await Assert.That(fixture.References.ContainsKey(Needed)).IsFalse();
    }

    /// <summary>Emits a dependency-bearing assembly without a compiler or build process.</summary>
    /// <param name="name">The assembly name.</param>
    /// <param name="source">The API declarations.</param>
    /// <param name="dependencies">Required dependency images.</param>
    /// <returns>The managed assembly image.</returns>
    /// <exception cref="InvalidOperationException">The fixture declarations do not compile.</exception>
    private static byte[] Compile(string name, string source, params byte[][] dependencies)
    {
        var references = new List<MetadataReference>(dependencies.Length + 1) { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) };
        for (var i = 0; i < dependencies.Length; i++)
        {
            references.Add(MetadataReference.CreateFromImage(dependencies[i]));
        }

        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)], references, new(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
        }

        return stream.ToArray();
    }

    /// <summary>Owns a local installed-package graph without performing restore or changing package caches.</summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>The synthetic package version.</summary>
        private const string PackageVersion = "1.0.0";

        /// <summary>The NuGet package library kind and nuspec root element.</summary>
        private const string PackageKind = "package";

        /// <summary>The nuspec package identifier element.</summary>
        private const string PackageIdElement = "id";

        /// <summary>The fixture's isolated package tree.</summary>
        private readonly ScratchDirectory _directory = new();

        /// <summary>Initializes a new instance of the <see cref="Fixture"/> class.</summary>
        /// <param name="tfm">The selected documentation framework.</param>
        public Fixture(string tfm)
        {
            Framework = NuGetFramework.ParseFolder(tfm);
            Target = new() { TargetFramework = Framework, RuntimeIdentifier = string.Empty };
            Assets.Targets.Add(Target);
            Assets.PackageFolders.Add(new(_directory.Path));
            References.Add(Path.GetFileNameWithoutExtension(typeof(object).Assembly.Location), typeof(object).Assembly.Location);
        }

        /// <summary>Gets the documentation framework.</summary>
        public NuGetFramework Framework { get; }

        /// <summary>Gets the restored target.</summary>
        public LockFileTarget Target { get; }

        /// <summary>Gets the restored package graph.</summary>
        public LockFile Assets { get; } = new();

        /// <summary>Gets the references available to API parsing.</summary>
        public Dictionary<string, string> References { get; } = [with(StringComparer.OrdinalIgnoreCase)];

        /// <summary>Adds a selected package and its declared files.</summary>
        /// <param name="id">The package identifier.</param>
        /// <param name="compileAssets">The compile assets selected by restore.</param>
        /// <param name="files">The installed package files.</param>
        public void AddPackage(string id, string[] compileAssets, params (string Path, byte[] Bytes)[] files)
        {
            var relative = $"{id.ToLowerInvariant()}/{PackageVersion}";
            var directory = Path.Combine(_directory.Path, relative);
            _ = Directory.CreateDirectory(directory);
            new XElement(
                PackageKind,
                new XElement(
                    "metadata",
                    new XElement(PackageIdElement, id),
                    new XElement("version", PackageVersion),
                    new XElement("authors", "Tests"),
                    new XElement("description", "Metadata fixture"))).Save(Path.Combine(directory, $"{id}.nuspec"));
            var filePaths = new string[files.Length];
            for (var i = 0; i < files.Length; i++)
            {
                var file = files[i];
                var path = Path.Combine(directory, file.Path);
                _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, file.Bytes);
                filePaths[i] = file.Path;
            }

            var package = new LockFileLibrary { Name = id, Version = NuGetVersion.Parse(PackageVersion), Type = PackageKind, Path = relative, Files = filePaths };
            var compileItems = new LockFileItem[compileAssets.Length];
            for (var i = 0; i < compileAssets.Length; i++)
            {
                var asset = compileAssets[i];
                compileItems[i] = new(asset);
                References.Add(Path.GetFileNameWithoutExtension(asset), Path.Combine(directory, asset));
            }

            var target = new LockFileTargetLibrary { Name = id, Version = package.Version, Type = PackageKind, CompileTimeAssemblies = compileItems };
            Assets.Libraries.Add(package);
            Target.Libraries.Add(target);
        }

        /// <summary>Adds the only documentation assembly.</summary>
        /// <param name="image">The root assembly image.</param>
        public void AddRoot(byte[] image)
        {
            var path = $"lib/{Framework.GetShortFolderName()}/{Root}.dll";
            AddPackage(Root, [path], (path, image));
        }

        /// <summary>Completes the root's metadata references.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Complete() => PackageMetadataReferences.Add(Assets, Root, Framework, null, References);

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose() => _directory.Dispose();
    }
}
