// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.TestHelpers;
using SourceDocParser.Zensical.Options;
using SourceDocParser.Zensical.Pages;

namespace SourceDocParser.Zensical.Tests;

/// <summary>
/// Pins <see cref="LandingPageEmitter"/> on the per-package and
/// per-namespace index page shape — file placement, content
/// fingerprints, and the cross-package isolation that prevents
/// clashing namespaces from overwriting each other.
/// </summary>
public class LandingPageEmitterTests
{
    /// <summary>One package index plus one namespace index per (package, namespace) bucket.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmitAllWritesPackageAndNamespaceIndexes()
    {
        using var temp = new TempDirectory();
        var foo = TestData.ObjectType("Foo", assemblyName: "Splat") with { Namespace = "Splat" };

        var written = LandingPageEmitter.EmitAll([foo], temp.Path, ZensicalEmitterOptions.Default);
        var packageIndex = await File.ReadAllTextAsync(Path.Combine(temp.Path, "Splat", LandingPageEmitter.IndexFileName));
        var namespaceIndex = await File.ReadAllTextAsync(Path.Combine(temp.Path, "Splat", "Splat", LandingPageEmitter.IndexFileName));

        await Assert.That(written).IsEqualTo(2);
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
        var core = TestData.ObjectType("Reactive", assemblyName: "ReactiveUI") with { Namespace = "ReactiveUI" };
        var wpf = TestData.ObjectType("WpfHelper", assemblyName: "ReactiveUI.Wpf") with { Namespace = "ReactiveUI" };

        LandingPageEmitter.EmitAll([core, wpf], temp.Path, ZensicalEmitterOptions.Default);
        var coreIndex = await File.ReadAllTextAsync(Path.Combine(temp.Path, "ReactiveUI", "ReactiveUI", LandingPageEmitter.IndexFileName));
        var wpfIndex = await File.ReadAllTextAsync(Path.Combine(temp.Path, "ReactiveUI.Wpf", "ReactiveUI", LandingPageEmitter.IndexFileName));

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
            new(FolderName: "Primary", AssemblyPrefix: "Primary"),
        ]);
        var matched = TestData.ObjectType("Foo", assemblyName: "Primary") with { Namespace = "Primary" };
        var skipped = TestData.ObjectType("Bar", assemblyName: "Other") with { Namespace = "Other" };

        var written = LandingPageEmitter.EmitAll([matched, skipped], temp.Path, options);

        await Assert.That(written).IsEqualTo(2);
        await Assert.That(Directory.Exists(Path.Combine(temp.Path, "Other"))).IsFalse();
    }
}
