// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.TestHelpers;
using SourceDocParser.Zensical.Options;
using SourceDocParser.Zensical.Pages;

namespace SourceDocParser.Zensical.Tests;

/// <summary>
/// Pins <see cref="LandingPageEmitter"/> on the per-package and per-namespace index page shape.
/// Drives the emitter end-to-end through <see cref="ZensicalDocumentationEmitter"/> and a
/// <see cref="FilePageSink"/> so the assertions are written against the on-disk layout the
/// landing emitter actually produces in production runs.
/// </summary>
public class LandingPageEmitterTests
{
    /// <summary>Fixture value for SplatPackage.</summary>
    private const string SplatPackage = "Splat";

    /// <summary>Fixture value for ReactiveUiPackage.</summary>
    private const string ReactiveUiPackage = "ReactiveUI";

    /// <summary>Fixture value for PrimaryPackage.</summary>
    private const string PrimaryPackage = "Primary";

    /// <summary>Fixture value for OtherPackage.</summary>
    private const string OtherPackage = "Other";

    /// <summary>Index types retain their own page and a working link from the namespace listing.</summary>
    /// <param name="typeName">Type name with the reserved landing-page spelling.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("Index")]
    [Arguments("index")]
    [Arguments("INDEX")]
    public async Task EmitAllSeparatesIndexTypeFromNamespacePage(string typeName)
    {
        const string NamespaceName = "System";
        using var temp = new TempDirectory();
        var type = TestData.ObjectType(typeName) with { Namespace = NamespaceName };

        await new ZensicalDocumentationEmitter().EmitAsync([type], new FilePageSink(temp.Path));
        var namespaceIndex = await File.ReadAllTextAsync(Path.Combine(temp.Path, "Test", NamespaceName, LandingPageEmitter.IndexFileName));

        await Assert.That(namespaceIndex).Contains($"[{typeName}]({typeName}-type.md)");
        var typePage = await File.ReadAllTextAsync(Path.Combine(temp.Path, "Test", NamespaceName, $"{typeName}-type.md"));
        await Assert.That(typePage).Contains($"# {typeName} class");
    }

    /// <summary>One package index plus one namespace index per (package, namespace) bucket.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmitAllWritesPackageAndNamespaceIndexes()
    {
        using var temp = new TempDirectory();
        var foo = TestData.ObjectType("Foo", assemblyName: SplatPackage) with { Namespace = SplatPackage };

        await new ZensicalDocumentationEmitter().EmitAsync([foo], new FilePageSink(temp.Path));
        var packageIndex = await File.ReadAllTextAsync(Path.Combine(temp.Path, SplatPackage, LandingPageEmitter.IndexFileName));
        var namespaceIndex = await File.ReadAllTextAsync(Path.Combine(temp.Path, SplatPackage, SplatPackage, LandingPageEmitter.IndexFileName));

        await Assert.That(packageIndex).Contains("# Splat package");
        await Assert.That(packageIndex).Contains("[Splat](Splat/index.md)");
        await Assert.That(namespaceIndex).Contains("# Splat namespace");
        await Assert.That(namespaceIndex).Contains("Part of the `Splat` package.");
        await Assert.That(namespaceIndex).Contains("| [Foo](Foo.md) | class |");
    }

    /// <summary>The same namespace name in two packages produces two distinct namespace index pages.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ClashingNamespacesAcrossPackagesEachGetTheirOwnIndex()
    {
        using var temp = new TempDirectory();
        var core = TestData.ObjectType("Reactive", assemblyName: ReactiveUiPackage) with { Namespace = ReactiveUiPackage };
        var wpf = TestData.ObjectType("WpfHelper", assemblyName: "ReactiveUI.Wpf") with { Namespace = ReactiveUiPackage };

        await new ZensicalDocumentationEmitter().EmitAsync([core, wpf], new FilePageSink(temp.Path));
        var coreIndex = await File.ReadAllTextAsync(Path.Combine(temp.Path, ReactiveUiPackage, ReactiveUiPackage, LandingPageEmitter.IndexFileName));
        var wpfIndex = await File.ReadAllTextAsync(Path.Combine(temp.Path, "ReactiveUI.Wpf", ReactiveUiPackage, LandingPageEmitter.IndexFileName));

        await Assert.That(coreIndex).Contains("[Reactive](Reactive.md)");
        await Assert.That(coreIndex).DoesNotContain("WpfHelper");
        await Assert.That(wpfIndex).Contains("[WpfHelper](WpfHelper.md)");
        await Assert.That(wpfIndex).DoesNotContain("[Reactive]");
    }

    /// <summary>Types from assemblies excluded by routing rules don't generate landing pages.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RoutingRulesFilterUnmatchedAssemblies()
    {
        using var temp = new TempDirectory();
        var options = new ZensicalEmitterOptions([
            new(FolderName: PrimaryPackage, AssemblyPrefix: PrimaryPackage),
        ]);
        var matched = TestData.ObjectType("Foo", assemblyName: PrimaryPackage) with { Namespace = PrimaryPackage };
        var skipped = TestData.ObjectType("Bar", assemblyName: OtherPackage) with { Namespace = OtherPackage };

        await new ZensicalDocumentationEmitter(options).EmitAsync([matched, skipped], new FilePageSink(temp.Path));

        await Assert.That(Directory.Exists(Path.Combine(temp.Path, OtherPackage))).IsFalse();
    }
}
