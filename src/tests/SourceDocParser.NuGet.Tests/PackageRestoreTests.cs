// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using SourceDocParser.LibCompilation;
using SourceDocParser.Model;
using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.NuGet.Tests;

/// <summary>Exercises package restore through documentation discovery.</summary>
public sealed class PackageRestoreTests
{
    /// <summary>The shared dependency assembly name.</summary>
    private const string Shared = "Shared";

    /// <summary>The dependency API used by parser-binding fixtures.</summary>
    private const string SharedSource = "public class SharedApi { }";

    /// <summary>The initial fixture package version.</summary>
    private const string InitialVersion = "1.0.0";

    /// <summary>A conflicting shared dependency version.</summary>
    private const string SecondVersion = "2.0.0";

    /// <summary>The default documentation framework.</summary>
    private const string Net10 = "net10.0";

    /// <summary>A portable documentation framework with a distinct dependency group.</summary>
    private const string Standard21 = "netstandard2.1";

    /// <summary>The documentation root assembly name.</summary>
    private const string Root = "Root";

    /// <summary>The transitive dependency assembly name.</summary>
    private const string Middle = "Middle";

    /// <summary>The first branch of a dependency graph.</summary>
    private const string Left = "Left";

    /// <summary>The second branch of a dependency graph.</summary>
    private const string Right = "Right";

    /// <summary>The compile asset selected for the shared dependency.</summary>
    private const string SharedRef = "ref/net10.0/Shared.dll";

    /// <summary>The shared dependency's implementation asset.</summary>
    private const string SharedLib = "lib/net10.0/Shared.dll";

    /// <summary>The expected reference asset for the second dependency generation.</summary>
    private const string SecondSharedReference = "/2.0.0/ref/net10.0/Shared.dll";

    /// <summary>The documentation root's implementation assembly.</summary>
    private const string RootLib = "lib/net10.0/Root.dll";

    /// <summary>The first branch's compile asset.</summary>
    private const string LeftLib = "lib/net10.0/Left.dll";

    /// <summary>The second branch's compile asset.</summary>
    private const string RightLib = "lib/net10.0/Right.dll";

    /// <summary>The lowest dependency generation.</summary>
    private const string FirstRange = "[1.0.0,2.0.0)";

    /// <summary>The next dependency generation.</summary>
    private const string SecondRange = "[2.0.0,3.0.0)";

    /// <summary>A broad compatible dependency range.</summary>
    private const string BroadRange = "[1.0.0,3.0.0)";

    /// <summary>The number of independently selected documentation roots.</summary>
    private const int TwoRoots = 2;

    /// <summary>The NuGet assets section containing resolved target graphs.</summary>
    private const string TargetsProperty = "targets";

    /// <summary>Excluded packages remain available as version-constrained compile references.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task ExcludedDependencyUsesDeclaredVersionAndReferenceAssets()
    {
        using var fixture = new PackageFeed();
        fixture.Add(Shared, InitialVersion, string.Empty, SharedRef);
        fixture.Add(Shared, SecondVersion, string.Empty, SharedRef);
        fixture.Add(Root, InitialVersion, fixture.Dependency(Shared, FirstRange), RootLib);
        fixture.Manifest([Root], [Shared]);

        var groups = await fixture.DiscoverAsync();

        await Assert.That(groups.Count).IsEqualTo(1);
        await Assert.That(groups[0].AssemblyPaths.Length).IsEqualTo(1);
        await Assert.That(Path.GetFileName(groups[0].AssemblyPaths[0])).IsEqualTo("Root.dll");
        await Assert.That(ReferencePath(groups[0], Shared)).Contains("/1.0.0/ref/net10.0/Shared.dll");
    }

    /// <summary>Transitive ranges come from the dependency group compatible with the selected framework.</summary>
    /// <param name="tfm">The framework selected for documentation.</param>
    /// <param name="version">The expected shared dependency version.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(Net10, InitialVersion)]
    [Arguments(Standard21, SecondVersion)]
    public async Task FrameworkSpecificGroupsRetainTransitiveVersionRanges(string tfm, string version)
    {
        using var fixture = new PackageFeed { TargetFramework = tfm };
        fixture.Add(Shared, InitialVersion, string.Empty, SharedRef, "ref/netstandard2.1/Shared.dll");
        fixture.Add(Shared, SecondVersion, string.Empty, SharedRef, "ref/netstandard2.1/Shared.dll");
        var dependencies = PackageFeed.Group(Net10, fixture.Dependency(Shared, FirstRange)) + PackageFeed.Group(Standard21, fixture.Dependency(Shared, SecondRange));
        fixture.Add(Middle, InitialVersion, dependencies, "lib/net10.0/Middle.dll", "lib/netstandard2.1/Middle.dll");
        fixture.Add(Root, InitialVersion, fixture.Dependency(Middle, InitialVersion), RootLib, "lib/netstandard2.1/Root.dll");
        fixture.Manifest([Root], []);

        var groups = await fixture.DiscoverAsync();

        await Assert.That(groups.Count).IsEqualTo(1);
        await Assert.That(groups[0].Tfm).IsEqualTo(tfm);
        await Assert.That(ReferencePath(groups[0], Shared)).Contains($"/{version}/ref/{tfm}/Shared.dll");
        await Assert.That(groups[0].FallbackIndex.ContainsKey(Middle)).IsTrue();
    }

    /// <summary>Compatible cousin dependency ranges select their lowest common version.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task CousinDependenciesSelectLowestCompatibleVersion()
    {
        using var fixture = new PackageFeed();
        fixture.Add(Shared, InitialVersion, string.Empty, SharedRef);
        fixture.Add(Shared, SecondVersion, string.Empty, SharedRef);
        fixture.Add(Shared, "2.5.0", string.Empty, SharedRef);
        fixture.Add(Left, InitialVersion, fixture.Dependency(Shared, BroadRange), LeftLib);
        fixture.Add(Right, InitialVersion, fixture.Dependency(Shared, SecondRange), RightLib);
        fixture.Add(Root, InitialVersion, fixture.Dependency(Left, InitialVersion) + fixture.Dependency(Right, InitialVersion), RootLib);
        fixture.Manifest([Root], []);

        var groups = await fixture.DiscoverAsync();

        await Assert.That(ReferencePath(groups[0], Shared)).Contains(SecondSharedReference);
    }

    /// <summary>The complete package compile graph matches an ordinary SDK PackageReference restore.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task ResolvedCompileGraphAgreesWithDotnetRestore()
    {
        using var fixture = new PackageFeed();
        fixture.Add(Shared, InitialVersion, string.Empty, SharedRef);
        fixture.Add(Shared, SecondVersion, string.Empty, SharedRef, SharedLib);
        fixture.Add(Left, InitialVersion, fixture.Dependency(Shared, BroadRange), LeftLib);
        fixture.Add(Right, InitialVersion, fixture.Dependency(Shared, SecondRange), RightLib);
        fixture.Add(Root, InitialVersion, fixture.Dependency(Left, InitialVersion) + fixture.Dependency(Right, InitialVersion), RootLib);
        fixture.Manifest([Root], []);

        var groups = await fixture.DiscoverAsync();
        using var normalRestore = await fixture.RestoreProjectAsync(Root);
        var assets = normalRestore.RootElement;
        Dictionary<string, string> expected = [with(StringComparer.OrdinalIgnoreCase)];
        foreach (var library in assets.GetProperty(TargetsProperty).GetProperty(Net10).EnumerateObject())
        {
            var packagePath = assets.GetProperty("libraries").GetProperty(library.Name).GetProperty("path").GetString();
            foreach (var compile in library.Value.GetProperty("compile").EnumerateObject())
            {
                expected.Add(Path.GetFileNameWithoutExtension(compile.Name), $"/{packagePath}/{compile.Name}");
            }
        }

        var packageReferenceCount = 0;
        foreach (var path in groups[0].FallbackIndex.Values)
        {
            if (fixture.IsFixturePackagePath(path))
            {
                packageReferenceCount++;
            }
        }

        await Assert.That(packageReferenceCount).IsEqualTo(expected.Count);
        foreach (var reference in expected)
        {
            await Assert.That(ReferencePath(groups[0], reference.Key)).EndsWith(reference.Value);
        }
    }

    /// <summary>NuGet reports disjoint cousin ranges with the package and dependency conflict code.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task IncompatibleCousinRangesReportNuGetConflict()
    {
        using var fixture = new PackageFeed();
        fixture.Add(Shared, InitialVersion, string.Empty, SharedRef);
        fixture.Add(Shared, SecondVersion, string.Empty, SharedRef);
        fixture.Add(Left, InitialVersion, fixture.Dependency(Shared, FirstRange), LeftLib);
        fixture.Add(Right, InitialVersion, fixture.Dependency(Shared, SecondRange), RightLib);
        fixture.Add(Root, InitialVersion, fixture.Dependency(Left, InitialVersion) + fixture.Dependency(Right, InitialVersion), RootLib);
        fixture.Manifest([Root], []);

        var exception = await Assert.That(async () => { _ = await fixture.DiscoverAsync(); }).Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("NU1107");
        await Assert.That(exception.Message).Contains(Shared);
        await Assert.That(exception.Message).Contains(Root);
    }

    /// <summary>A dependency pin participates as a direct reference and controls the selected version.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task ExplicitPinHasDirectDependencyPrecedence()
    {
        using var fixture = new PackageFeed();
        fixture.Add(Shared, InitialVersion, string.Empty, SharedRef);
        fixture.Add(Shared, SecondVersion, string.Empty, SharedRef);
        fixture.Add(Root, InitialVersion, fixture.Dependency(Shared, FirstRange), RootLib);
        fixture.DependencyPins.Add(Shared, SecondVersion);
        fixture.Manifest([Root], []);

        var groups = await fixture.DiscoverAsync();

        await Assert.That(groups.Count).IsEqualTo(1);
        await Assert.That(ReferencePath(groups[0], Shared)).Contains(SecondSharedReference);
    }

    /// <summary>Root and dependency package pins match package identifiers without case sensitivity.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task PackagePinsMatchCaseInsensitiveIdentifiers()
    {
        using var fixture = new PackageFeed();
        fixture.Add(Shared, InitialVersion, string.Empty, SharedRef);
        fixture.Add(Shared, SecondVersion, string.Empty, SharedRef);
        fixture.Add(Root, InitialVersion, fixture.Dependency(Shared, BroadRange), RootLib);
        fixture.DependencyPins.Add(Root.ToUpperInvariant(), InitialVersion);
        fixture.DependencyPins.Add(Shared.ToUpperInvariant(), SecondVersion);
        fixture.Manifest([Root], []);

        var groups = await fixture.DiscoverAsync();

        await Assert.That(groups.Count).IsEqualTo(1);
        await Assert.That(ReferencePath(groups[0], Shared)).Contains(SecondSharedReference);
    }

    /// <summary>A package's TFM override applies regardless of the spelling used for its identifier.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task FrameworkOverrideMatchesCaseInsensitivePackageIdentifier()
    {
        using var fixture = new PackageFeed();
        fixture.Add(Root, InitialVersion, string.Empty, RootLib, "lib/netstandard2.1/Root.dll");
        fixture.TfmOverrides.Add(Root.ToUpperInvariant(), Standard21);
        fixture.Manifest([Root], []);

        var groups = await fixture.DiscoverAsync();

        await Assert.That(groups.Count).IsEqualTo(1);
        await Assert.That(groups[0].Tfm).IsEqualTo(Standard21);
    }

    /// <summary>Identical compile assemblies bundled by a root and dependency coalesce without losing the documented root.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task IdenticalCompileAssembliesAcrossPackagesCoalesce()
    {
        using var fixture = new PackageFeed();
        var assembly = PackageFeed.Compile(Shared, SharedSource);
        fixture.AddAssembly(Shared, InitialVersion, string.Empty, assembly, [SharedRef]);
        fixture.AddAssembly(Root, InitialVersion, fixture.Dependency(Shared, InitialVersion), assembly, [SharedRef]);
        fixture.Manifest([Root], []);

        var groups = await fixture.DiscoverAsync();

        await Assert.That(groups.Count).IsEqualTo(1);
        await Assert.That(groups[0].AssemblyPaths.Length).IsEqualTo(1);
        await Assert.That(groups[0].FallbackIndex.ContainsKey(Shared)).IsTrue();
        using var loader = new CompilationLoader();
        var loaded = loader.Load(groups[0].AssemblyPaths[0], groups[0].FallbackIndex);
        await Assert.That(loaded.Assembly.Name).IsEqualTo(Shared);
    }

    /// <summary>Different compile assembly contents with the same filename report their graph ambiguity.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task DifferentCompileAssembliesWithSameFilenameReportAmbiguity()
    {
        using var fixture = new PackageFeed();
        fixture.Add(Shared, InitialVersion, string.Empty, SharedRef);
        fixture.Add(Root, InitialVersion, fixture.Dependency(Shared, InitialVersion), SharedRef);
        fixture.Manifest([Root], []);

        var exception = await Assert.That(async () => { _ = await fixture.DiscoverAsync(); }).Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("ambiguous compile assembly");
        await Assert.That(exception.Message).Contains(Shared);
        await Assert.That(exception.Message).Contains("project.assets.json");
    }

    /// <summary>A nonfloating root range can use a cached package when the configured feed has no candidate.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task RootRangeUsesGlobalCacheWhenFeedHasNoCandidate()
    {
        using var fixture = new PackageFeed();
        fixture.Add(Root, InitialVersion, string.Empty, RootLib);
        fixture.Manifest([Root], []);
        _ = await fixture.DiscoverAsync();
        fixture.RemovePackageFromFeed(Root, InitialVersion);
        fixture.RootVersion = FirstRange;
        fixture.Manifest([Root], []);

        var groups = await fixture.DiscoverAsync();
        using var normal = await fixture.RestoreProjectAsync(Root);

        await Assert.That(groups.Count).IsEqualTo(1);
        await Assert.That(groups[0].AssemblyPaths[0].Replace('\\', '/')).Contains("/1.0.0/");
        await Assert.That(normal.RootElement.GetProperty(TargetsProperty).GetProperty(Net10).EnumerateObject().MoveNext()).IsTrue();
    }

    /// <summary>A remote lower version competes with a higher cached version using the same selection as SDK restore.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task RootRangeSelectsSameVersionAsSdkWithHigherCachedPackage()
    {
        using var fixture = new PackageFeed { RootVersion = SecondVersion };
        fixture.Add(Root, InitialVersion, string.Empty, RootLib);
        fixture.Add(Root, SecondVersion, string.Empty, RootLib);
        fixture.Manifest([Root], []);
        _ = await fixture.DiscoverAsync();
        fixture.RemovePackageFromFeed(Root, SecondVersion);
        fixture.RootVersion = BroadRange;
        fixture.Manifest([Root], []);

        using var normal = await fixture.RestoreProjectAsync(Root);
        var groups = await fixture.DiscoverAsync();
        var target = normal.RootElement.GetProperty(TargetsProperty).GetProperty(Net10);
        foreach (var library in target.EnumerateObject())
        {
            var selectedVersion = library.Name[(library.Name.IndexOf('/') + 1)..];
            await Assert.That(groups[0].AssemblyPaths[0].Replace('\\', '/')).Contains($"/{selectedVersion}/");
        }
    }

    /// <summary>An unpinned root follows the feed's latest stable version despite an older cached root.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task UnpinnedRootSelectsLatestStableAfterCacheWarmup()
    {
        using var fixture = new PackageFeed();
        fixture.Add(Root, InitialVersion, string.Empty, RootLib);
        fixture.Manifest([Root], []);
        _ = await fixture.DiscoverAsync();
        fixture.Add(Root, SecondVersion, string.Empty, RootLib);
        fixture.Add(Root, "3.0.0-preview", string.Empty, RootLib);
        fixture.RootVersion = null;
        fixture.Manifest([Root], []);

        var groups = await fixture.DiscoverAsync();

        await Assert.That(groups[0].AssemblyPaths[0].Replace('\\', '/')).Contains("/2.0.0/");
    }

    /// <summary>A mapped source controls dependency versions even when another configured feed has a lower version.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task PackageSourceMappingSelectsDependencyFromAllowedFeed()
    {
        using var fixture = new PackageFeed();
        fixture.Add(Shared, InitialVersion, string.Empty, SharedRef);
        fixture.MovePackageToOtherFeed(Shared, InitialVersion);
        fixture.Add(Shared, SecondVersion, string.Empty, SharedRef);
        fixture.Add(Root, InitialVersion, fixture.Dependency(Shared, BroadRange), RootLib);
        fixture.ConfigureSourceMapping();
        fixture.Manifest([Root], []);

        var groups = await fixture.DiscoverAsync();

        await Assert.That(ReferencePath(groups[0], Shared)).Contains(SecondSharedReference);
    }

    /// <summary>A package available only in an unmapped feed cannot satisfy a dependency.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task PackageSourceMappingRejectsDependencyFromUnmappedFeed()
    {
        using var fixture = new PackageFeed();
        fixture.Add(Shared, InitialVersion, string.Empty, SharedRef);
        fixture.MovePackageToOtherFeed(Shared, InitialVersion);
        fixture.Add(Root, InitialVersion, fixture.Dependency(Shared, InitialVersion), RootLib);
        fixture.ConfigureSourceMapping();
        fixture.Manifest([Root], []);

        var exception = await Assert.That(async () => { _ = await fixture.DiscoverAsync(); }).Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("NU1101");
        await Assert.That(exception.Message).Contains(Shared);
        await Assert.That(exception.Message).Contains("PackageSourceMapping");
    }

    /// <summary>Floating pins and explicit prerelease ranges follow NuGet version selection.</summary>
    /// <param name="range">The direct dependency constraint.</param>
    /// <param name="expected">The expected package version.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments("1.*", "1.5.0")]
    [Arguments("[1.1.0-beta,1.5.0)", "1.1.0-beta")]
    public async Task PinsPreserveFloatingAndPrereleaseSemantics(string range, string expected)
    {
        using var fixture = new PackageFeed();
        fixture.Add(Shared, InitialVersion, string.Empty, SharedRef);
        fixture.Add(Shared, "1.1.0-beta", string.Empty, SharedRef);
        fixture.Add(Shared, "1.5.0", string.Empty, SharedRef);
        fixture.Add(Shared, SecondVersion, string.Empty, SharedRef);
        fixture.Add(Root, InitialVersion, fixture.Dependency(Shared, BroadRange), RootLib);
        fixture.DependencyPins.Add(Shared, range);
        fixture.Manifest([Root], []);

        var groups = await fixture.DiscoverAsync();

        await Assert.That(ReferencePath(groups[0], Shared)).Contains($"/{expected}/ref/net10.0/Shared.dll");
    }

    /// <summary>A missing transitive package reports the root, framework, and NuGet diagnostic.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task MissingTransitivePackageReportsActionableRestoreDiagnostic()
    {
        using var fixture = new PackageFeed();
        fixture.Add(Middle, InitialVersion, fixture.Dependency("Missing", InitialVersion), "lib/net10.0/Middle.dll");
        fixture.Add(Root, InitialVersion, fixture.Dependency(Middle, InitialVersion), RootLib);
        fixture.Manifest([Root], []);

        var exception = await Assert.That(async () => { _ = await fixture.DiscoverAsync(); }).Throws<InvalidOperationException>();

        await Assert.That(exception!.Message).Contains("NU1101");
        await Assert.That(exception.Message).Contains("Missing");
        await Assert.That(exception.Message).Contains(Root);
        await Assert.That(exception.Message).Contains(Net10);
    }

    /// <summary>Compile assets prefer ref groups and ignore arbitrary runtime assemblies.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task CompileAssetsPreferRefOverLibAndRuntime()
    {
        using var fixture = new PackageFeed { RuntimeIdentifier = "linux-x64" };
        fixture.Add(Shared, InitialVersion, string.Empty, SharedRef, SharedLib, "runtimes/linux-x64/lib/net10.0/RuntimeOnly.dll");
        fixture.Add(Root, InitialVersion, fixture.Dependency(Shared, InitialVersion), RootLib, "ref/net10.0/Root.dll");
        fixture.Manifest([Root], []);

        var groups = await fixture.DiscoverAsync();

        await Assert.That(groups[0].AssemblyPaths.Length).IsEqualTo(1);
        await Assert.That(groups[0].AssemblyPaths[0].Replace('\\', '/')).EndsWith("/ref/net10.0/Root.dll");
        await Assert.That(ReferencePath(groups[0], Shared)).EndsWith("/ref/net10.0/Shared.dll");
        await Assert.That(groups[0].FallbackIndex.ContainsKey("RuntimeOnly")).IsFalse();
    }

    /// <summary>An explicit empty compile group prevents implementation-only dependencies from becoming references.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task EmptyRefGroupDoesNotFallBackToLib()
    {
        using var fixture = new PackageFeed();
        fixture.Add(Shared, InitialVersion, string.Empty, "ref/net10.0/_._", SharedLib);
        fixture.Add(Root, InitialVersion, fixture.Dependency(Shared, InitialVersion), RootLib);
        fixture.Manifest([Root], []);

        var groups = await fixture.DiscoverAsync();

        await Assert.That(groups.Count).IsEqualTo(1);
        await Assert.That(groups[0].FallbackIndex.ContainsKey(Shared)).IsFalse();
    }

    /// <summary>Separate roots keep incompatible dependency generations in separate assembly groups.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task DocumentationRootsKeepIncompatibleDependencyGraphsIsolated()
    {
        using var fixture = new PackageFeed();
        fixture.Add(Shared, InitialVersion, string.Empty, SharedRef);
        fixture.Add(Shared, SecondVersion, string.Empty, SharedRef);
        fixture.Add(Left, InitialVersion, fixture.Dependency(Shared, FirstRange), LeftLib);
        fixture.Add(Right, InitialVersion, fixture.Dependency(Shared, SecondRange), RightLib);
        fixture.Manifest([Left, Right], []);

        var groups = await fixture.DiscoverAsync();

        await Assert.That(groups.Count).IsEqualTo(TwoRoots);
        foreach (var group in groups)
        {
            await Assert.That(group.AssemblyPaths.Length).IsEqualTo(1);
            var version = Path.GetFileNameWithoutExtension(group.AssemblyPaths[0]) is Left ? InitialVersion : SecondVersion;
            await Assert.That(ReferencePath(group, Shared)).Contains($"/{version}/ref/net10.0/Shared.dll");
        }
    }

    /// <summary>Warm cache discovery produces the same graph as the first restore.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task WarmCachePreservesResolvedReferences()
    {
        using var fixture = new PackageFeed();
        fixture.Add(Shared, InitialVersion, string.Empty, SharedRef);
        fixture.Add(Root, InitialVersion, fixture.Dependency(Shared, InitialVersion), RootLib);
        fixture.Manifest([Root], []);

        var first = await fixture.DiscoverAsync();
        var second = await fixture.DiscoverAsync();

        await Assert.That(second.Count).IsEqualTo(first.Count);
        await Assert.That(second[0].AssemblyPaths).IsEquivalentTo(first[0].AssemblyPaths);
        await Assert.That(second[0].FallbackIndex).IsEquivalentTo(first[0].FallbackIndex);
    }

    /// <summary>A fetcher decorator preserves the documentation roots and their individual reference sets.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task FetcherDecoratorPreservesRestoredGroups()
    {
        using var fixture = new PackageFeed();
        fixture.Add(Shared, InitialVersion, string.Empty, SharedRef);
        fixture.Add(Root, InitialVersion, fixture.Dependency(Shared, InitialVersion), RootLib);
        fixture.Manifest([Root], [Shared]);
        var direct = await fixture.DiscoverAsync();

        fixture.DecorateFetcher = true;
        var decorated = await fixture.DiscoverAsync();

        await Assert.That(decorated.Count).IsEqualTo(direct.Count);
        await Assert.That(decorated[0].AssemblyPaths).IsEquivalentTo(direct[0].AssemblyPaths);
        await Assert.That(decorated[0].FallbackIndex).IsEquivalentTo(direct[0].FallbackIndex);
    }

    /// <summary>Empty restore results prevent obsolete lib files from becoming documentation roots.</summary>
    /// <param name="remove">Whether to remove the root declaration instead of excluding it.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task DecoratedFetcherDoesNotReviveRemovedOrExcludedRoots(bool remove)
    {
        using var fixture = new PackageFeed();
        fixture.Add(Root, InitialVersion, string.Empty, RootLib);
        fixture.Manifest([Root], []);
        var first = await fixture.DiscoverAsync();
        fixture.SeedLegacyAssembly(first[0].AssemblyPaths[0]);
        fixture.DecorateFetcher = true;
        fixture.Manifest(remove ? [] : [Root], remove ? [] : [Root]);

        var groups = await fixture.DiscoverAsync();

        await Assert.That(groups).IsEmpty();
    }

    /// <summary>Changing dependency pins invalidates the graph without changing the package root.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task ChangedPinSelectsNewGraphWithWarmCache()
    {
        using var fixture = new PackageFeed();
        fixture.Add(Shared, InitialVersion, string.Empty, SharedRef);
        fixture.Add(Shared, SecondVersion, string.Empty, SharedRef);
        fixture.Add(Root, InitialVersion, fixture.Dependency(Shared, BroadRange), RootLib);
        fixture.DependencyPins.Add(Shared, InitialVersion);
        fixture.Manifest([Root], []);
        var first = await fixture.DiscoverAsync();

        fixture.DependencyPins[Shared] = SecondVersion;
        fixture.Manifest([Root], []);
        var second = await fixture.DiscoverAsync();

        await Assert.That(ReferencePath(first[0], Shared)).Contains("/1.0.0/");
        await Assert.That(ReferencePath(second[0], Shared)).Contains("/2.0.0/");
    }

    /// <summary>Cached roots excluded by a changed manifest cannot return as documentation inputs.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task ExcludedCachedRootRemainsReferenceOnly()
    {
        using var fixture = new PackageFeed();
        fixture.Add(Shared, InitialVersion, string.Empty, SharedRef);
        fixture.Add(Root, InitialVersion, fixture.Dependency(Shared, InitialVersion), RootLib);
        fixture.Manifest([Root, Shared], []);
        var first = await fixture.DiscoverAsync();
        await Assert.That(first.Count).IsEqualTo(TwoRoots);

        fixture.Manifest([Root, Shared], [Shared]);
        var second = await fixture.DiscoverAsync();

        await Assert.That(second.Count).IsEqualTo(1);
        await Assert.That(Path.GetFileNameWithoutExtension(second[0].AssemblyPaths[0])).IsEqualTo(Root);
        await Assert.That(second[0].FallbackIndex.ContainsKey(Shared)).IsTrue();
    }

    /// <summary>Platform dependency groups select the corresponding platform package and compatible ref assets.</summary>
    /// <param name="tfm">The platform framework.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments("net10.0-windows10.0.19041")]
    [Arguments("net10.0-android36.0")]
    public async Task PlatformFrameworkSelectsApplicableDependencyGroup(string tfm)
    {
        using var fixture = new PackageFeed { TargetFramework = tfm };
        fixture.ConfigurePlatformReferenceFeed();
        fixture.Add(Shared, InitialVersion, string.Empty, $"ref/{tfm}/Shared.dll");
        var dependencies = PackageFeed.Group(tfm, fixture.Dependency(Shared, InitialVersion)) + PackageFeed.Group(Net10, fixture.Dependency("Unrelated", InitialVersion));
        fixture.Add(Root, InitialVersion, dependencies, $"lib/{tfm}/Root.dll");
        fixture.Manifest([Root], [Shared]);

        var groups = await fixture.DiscoverAsync();

        await Assert.That(groups.Count).IsEqualTo(1);
        await Assert.That(groups[0].Tfm).IsEqualTo(tfm);
        await Assert.That(ReferencePath(groups[0], Shared)).EndsWith($"/ref/{tfm}/Shared.dll");
        await Assert.That(groups[0].FallbackIndex.ContainsKey("Unrelated")).IsFalse();
    }

    /// <summary>Restored references bind base classes and property types when the documented assembly is parsed.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task RestoredCompileReferencesReachApiParsing()
    {
        using var fixture = new PackageFeed();
        var shared = PackageFeed.Compile(Shared, SharedSource);
        var root = PackageFeed.Compile(Root, "public sealed class RootApi : SharedApi { public SharedApi Value => this; }", shared);
        fixture.AddAssembly(Shared, InitialVersion, string.Empty, shared, [SharedRef]);
        fixture.AddAssembly(Root, InitialVersion, fixture.Dependency(Shared, InitialVersion), root, [RootLib]);
        fixture.Manifest([Root], [Shared]);
        var groups = await fixture.DiscoverAsync();

        using var loader = new CompilationLoader();
        var loaded = loader.Load(groups[0].AssemblyPaths[0], groups[0].FallbackIndex);
        var api = loaded.Assembly.GetTypeByMetadataName("RootApi");

        await Assert.That(api).IsNotNull();
        await Assert.That(api!.BaseType!.TypeKind).IsEqualTo(TypeKind.Class);
        await Assert.That(api.BaseType.ContainingAssembly.Name).IsEqualTo(Shared);
        var property = (IPropertySymbol)api.GetMembers("Value")[0];
        await Assert.That(property.Type.TypeKind).IsEqualTo(TypeKind.Class);
        await Assert.That(property.Type.ContainingAssembly.Name).IsEqualTo(Shared);
    }

    /// <summary>A package build target can supply an API dependency outside NuGet's compile asset directories.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task PackageBuildTargetReferencesReachApiParsing()
    {
        using var fixture = new PackageFeed();
        var shared = PackageFeed.Compile(Shared, SharedSource);
        var root = PackageFeed.Compile(Root, "public sealed class RootApi : SharedApi { }", shared);
        fixture.AddAssembly(Root, InitialVersion, string.Empty, root, [RootLib]);
        fixture.AddBuildReference(Root, shared);
        fixture.Manifest([Root], []);

        var groups = await fixture.DiscoverAsync();

        await Assert.That(groups[0].AssemblyPaths.Length).IsEqualTo(1);
        await Assert.That(groups[0].FallbackIndex.ContainsKey(Shared)).IsTrue();
        await Assert.That(ReferencePath(groups[0], Shared)).EndsWith("/manual/Shared.dll");
        using var loader = new CompilationLoader();
        var loaded = loader.Load(groups[0].AssemblyPaths[0], groups[0].FallbackIndex);
        await Assert.That(loaded.Assembly.GetTypeByMetadataName("RootApi")!.BaseType!.TypeKind).IsEqualTo(TypeKind.Class);
    }

    /// <summary>Repeated discovery reuses the completed build-reference evaluation without rewriting its SDK project.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task PackageBuildReferenceEvaluationIsCached()
    {
        using var fixture = new PackageFeed();
        fixture.Add(Root, InitialVersion, string.Empty, RootLib);
        fixture.AddBuildReference(Root, PackageFeed.Compile(Shared, SharedSource));
        fixture.Manifest([Root], []);
        var first = await fixture.DiscoverAsync();
        var project = fixture.GetEvaluationProjectPath();
        File.SetLastWriteTimeUtc(project, DateTime.UnixEpoch);

        var second = await fixture.DiscoverAsync();

        await Assert.That(second[0].FallbackIndex).IsEquivalentTo(first[0].FallbackIndex);
        await Assert.That(File.GetLastWriteTimeUtc(project)).IsEqualTo(DateTime.UnixEpoch);
    }

    /// <summary>A relative API output path remains valid when the package configuration lives outside the process directory.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task PackageBuildReferencesSupportRelativeApiPath()
    {
        using var fixture = new PackageFeed { UseRelativeApiPath = true };
        fixture.Add(Root, InitialVersion, string.Empty, RootLib);
        fixture.AddBuildReference(Root, PackageFeed.Compile(Shared, SharedSource));
        fixture.Manifest([Root], []);

        var groups = await fixture.DiscoverAsync();

        await Assert.That(groups.Count).IsEqualTo(1);
        await Assert.That(groups[0].FallbackIndex.ContainsKey(Shared)).IsTrue();
        await Assert.That(Path.IsPathFullyQualified(fixture.GetEvaluationProjectPath())).IsTrue();
    }

    /// <summary>Package reference targets retain their platform conditions when only metadata is required from a workload framework.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task PackageBuildReferencesPreservePlatformConditionsWithoutBuildTools()
    {
        using var fixture = new PackageFeed { TargetFramework = "net10.0-ios26.0" };
        fixture.ConfigurePlatformReferenceFeed();
        fixture.Add(Root, InitialVersion, string.Empty, $"lib/{fixture.TargetFramework}/Root.dll");
        fixture.AddBuildReference(Root, PackageFeed.Compile(Shared, SharedSource));
        fixture.Manifest([Root], []);

        var groups = await fixture.DiscoverAsync();

        await Assert.That(groups.Count).IsEqualTo(1);
        await Assert.That(groups[0].Tfm).IsEqualTo(fixture.TargetFramework);
        await Assert.That(groups[0].FallbackIndex.ContainsKey(Shared)).IsTrue();
    }

    /// <summary>Normalizes package paths for portable assertions.</summary>
    /// <param name="group">The restored assembly group.</param>
    /// <param name="name">The assembly simple name.</param>
    /// <returns>The selected reference path.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string ReferencePath(AssemblyGroup group, string name) => group.FallbackIndex[name].Replace('\\', '/');

    /// <summary>A local NuGet feed with independent package identities.</summary>
    private sealed class PackageFeed : IDisposable
    {
        /// <summary>The directory and source name for the allowed package feed.</summary>
        private const string FeedDirectory = "feed";

        /// <summary>The directory and source name for the unmapped feed.</summary>
        private const string OtherFeedDirectory = "unmapped";

        /// <summary>The local feed's NuGet source name.</summary>
        private const string FixtureSource = "fixture";

        /// <summary>The configuration consumed by both restore implementations.</summary>
        private const string NuGetConfigFile = "nuget.config";

        /// <summary>The fixture's restore output directory.</summary>
        private const string ApiDirectory = "api";

        /// <summary>The XML element declaring a package source.</summary>
        private const string AddSourceElement = "add";

        /// <summary>The XML attribute identifying a package source.</summary>
        private const string SourceKeyAttribute = "key";

        /// <summary>The XML attribute specifying a package source location.</summary>
        private const string SourceValueAttribute = "value";

        /// <summary>The XML section declaring configured feeds.</summary>
        private const string PackageSourcesElement = "packageSources";

        /// <summary>The XML mapping element identifying a feed.</summary>
        private const string PackageSourceElement = "packageSource";

        /// <summary>The XML mapping element identifying package patterns.</summary>
        private const string PackageElement = "package";

        /// <summary>The XML attribute containing a package pattern.</summary>
        private const string PatternAttribute = "pattern";

        /// <summary>The fixture files.</summary>
        private readonly ScratchDirectory _directory = new("sdp-restore");

        /// <summary>A unique namespace for packages in the central cache.</summary>
        private readonly string _prefix = $"sdp.test.{Guid.NewGuid():N}.";

        /// <summary>Initializes a new instance of the <see cref="PackageFeed"/> class.</summary>
        public PackageFeed()
        {
            var feed = Path.Combine(_directory.Path, FeedDirectory);
            _ = Directory.CreateDirectory(feed);
            var source = new XElement(AddSourceElement, new XAttribute(SourceKeyAttribute, FixtureSource), new XAttribute(SourceValueAttribute, feed));
            var sources = new XElement(PackageSourcesElement, new XElement("clear"), source);
            new XDocument(new XElement("configuration", sources))
                .Save(Path.Combine(_directory.Path, NuGetConfigFile));
        }

        /// <summary>Gets or sets the selected documentation framework.</summary>
        public string TargetFramework { get; set; } = Net10;

        /// <summary>Gets or sets the documentation root's requested version.</summary>
        public string? RootVersion { get; set; } = InitialVersion;

        /// <summary>Gets or sets a value indicating whether discovery uses a delegating fetcher.</summary>
        public bool DecorateFetcher { get; set; }

        /// <summary>Gets or sets a value indicating whether the API path is relative to the calling process.</summary>
        public bool UseRelativeApiPath { get; set; }

        /// <summary>Gets or sets the target runtime identifier.</summary>
        public string? RuntimeIdentifier { get; set; }

        /// <summary>Gets the explicitly selected dependency versions.</summary>
        public Dictionary<string, string> DependencyPins { get; } = [with(StringComparer.Ordinal)];

        /// <summary>Gets the frameworks selected for individual documentation roots.</summary>
        public Dictionary<string, string> TfmOverrides { get; } = [with(StringComparer.Ordinal)];

        /// <summary>Creates a target-framework-specific dependency group.</summary>
        /// <param name="tfm">The declaring target framework.</param>
        /// <param name="dependencies">The dependency XML.</param>
        /// <returns>The nuspec group XML.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Group(string tfm, string dependencies) => $"<group targetFramework=\"{tfm}\">{dependencies}</group>";

        /// <summary>Compiles an assembly with optional references to fixture assemblies.</summary>
        /// <param name="name">The assembly identity.</param>
        /// <param name="source">The public API source.</param>
        /// <param name="dependencies">The referenced assemblies.</param>
        /// <returns>The compiled assembly bytes.</returns>
        /// <exception cref="InvalidOperationException">The fixture assembly cannot compile.</exception>
        public static byte[] Compile(string name, string source, params byte[][] dependencies)
        {
            var references = new MetadataReference[dependencies.Length + 1];
            references[0] = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
            for (var i = 0; i < dependencies.Length; i++)
            {
                references[i + 1] = MetadataReference.CreateFromImage(dependencies[i]);
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

        /// <summary>Identifies an assembly provided by this fixture's packages.</summary>
        /// <param name="path">The resolved reference path.</param>
        /// <returns>True for a fixture package reference.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsFixturePackagePath(string path) => path.Contains(_prefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>Moves a package into the alternate configured source.</summary>
        /// <param name="id">Fixture package name.</param>
        /// <param name="version">Package version.</param>
        public void MovePackageToOtherFeed(string id, string version)
        {
            var otherFeed = Path.Combine(_directory.Path, OtherFeedDirectory);
            _ = Directory.CreateDirectory(otherFeed);
            var fileName = $"{_prefix}{id}.{version}.nupkg";
            File.Move(Path.Combine(_directory.Path, FeedDirectory, fileName), Path.Combine(otherFeed, fileName));
        }

        /// <summary>Removes a fixture package from the feed while preserving NuGet's installed cache entry.</summary>
        /// <param name="id">Fixture package name.</param>
        /// <param name="version">Package version.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemovePackageFromFeed(string id, string version) => File.Delete(Path.Combine(_directory.Path, FeedDirectory, $"{_prefix}{id}.{version}.nupkg"));

        /// <summary>Places a root assembly in the legacy extraction directory.</summary>
        /// <param name="source">The root assembly to copy.</param>
        public void SeedLegacyAssembly(string source)
        {
            var directory = Path.Combine(_directory.Path, ApiDirectory, "lib", Net10);
            _ = Directory.CreateDirectory(directory);
            File.Copy(source, Path.Combine(directory, Path.GetFileName(source)));
        }

        /// <summary>Maps fixture package identities to one of two configured feeds.</summary>
        public void ConfigureSourceMapping()
        {
            var primary = new XElement(AddSourceElement, new XAttribute(SourceKeyAttribute, FixtureSource), new XAttribute(SourceValueAttribute, Path.Combine(_directory.Path, FeedDirectory)));
            var secondaryPath = Path.Combine(_directory.Path, OtherFeedDirectory);
            var secondary = new XElement(AddSourceElement, new XAttribute(SourceKeyAttribute, OtherFeedDirectory), new XAttribute(SourceValueAttribute, secondaryPath));
            var sources = new XElement(PackageSourcesElement, new XElement("clear"), primary, secondary);
            var mapping = new XElement(PackageSourceElement, new XAttribute(SourceKeyAttribute, FixtureSource), new XElement(PackageElement, new XAttribute(PatternAttribute, $"{_prefix}*")));
            new XDocument(new XElement("configuration", sources, new XElement("packageSourceMapping", mapping))).Save(Path.Combine(_directory.Path, NuGetConfigFile));
        }

        /// <summary>Allows framework targeting packs from NuGet.org while keeping fixture packages on their local feed.</summary>
        public void ConfigurePlatformReferenceFeed()
        {
            var path = Path.Combine(_directory.Path, NuGetConfigFile);
            var document = XDocument.Load(path);
            var sources = document.Root!.Element(PackageSourcesElement)!;
            sources.Add(new XElement(AddSourceElement, new XAttribute(SourceKeyAttribute, "frameworks"), new XAttribute(SourceValueAttribute, "https://api.nuget.org/v3/index.json")));
            var fixtureMapping = new XElement(PackageSourceElement, new XAttribute(SourceKeyAttribute, FixtureSource), new XElement(PackageElement, new XAttribute(PatternAttribute, $"{_prefix}*")));
            var frameworkMapping = new XElement(PackageSourceElement, new XAttribute(SourceKeyAttribute, "frameworks"), new XElement(PackageElement, new XAttribute(PatternAttribute, "*")));
            document.Root.Add(new XElement("packageSourceMapping", fixtureMapping, frameworkMapping));
            document.Save(path);
        }

        /// <summary>Declares a package dependency.</summary>
        /// <param name="id">Fixture package name.</param>
        /// <param name="range">NuGet version range.</param>
        /// <returns>The dependency element.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string Dependency(string id, string range) => new XElement("dependency", new XAttribute(nameof(id), _prefix + id), new XAttribute("version", range)).ToString();

        /// <summary>Creates a package in the local feed.</summary>
        /// <param name="id">Fixture package name.</param>
        /// <param name="version">Package version.</param>
        /// <param name="dependencies">Dependency XML.</param>
        /// <param name="assets">Package asset paths.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(string id, string version, string dependencies, params string[] assets) => AddAssembly(id, version, dependencies, Compile(id, $"public class {id}Api {{ }}"), assets);

        /// <summary>Creates a package with a specific assembly in the local feed.</summary>
        /// <param name="id">Fixture package name.</param>
        /// <param name="version">Package version.</param>
        /// <param name="dependencies">Dependency XML.</param>
        /// <param name="assembly">The assembly contents.</param>
        /// <param name="assets">Package asset paths.</param>
        public void AddAssembly(string id, string version, string dependencies, byte[] assembly, string[] assets)
        {
            using var zip = ZipFile.Open(Path.Combine(_directory.Path, FeedDirectory, $"{_prefix}{id}.{version}.nupkg"), ZipArchiveMode.Create);
            using (var writer = new StreamWriter(zip.CreateEntry($"{_prefix}{id}.nuspec").Open()))
            {
                writer.Write($"<package><metadata><id>{_prefix}{id}</id><version>{version}</version><authors>Tests</authors>");
                writer.Write($"<description>Restore fixture</description><dependencies>{dependencies}</dependencies></metadata></package>");
            }

            foreach (var asset in assets)
            {
                using var stream = zip.CreateEntry(asset).Open();
                if (!asset.EndsWith(".dll", StringComparison.Ordinal))
                {
                    continue;
                }

                stream.Write(assembly);
            }
        }

        /// <summary>Adds a reference supplied by an evaluated package target.</summary>
        /// <param name="id">The fixture package containing the target.</param>
        /// <param name="assembly">The reference assembly bytes.</param>
        public void AddBuildReference(string id, byte[] assembly)
        {
            var path = Path.Combine(_directory.Path, FeedDirectory, $"{_prefix}{id}.{InitialVersion}.nupkg");
            using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
            using (var stream = archive.CreateEntry("manual/Shared.dll").Open())
            {
                stream.Write(assembly);
            }

            var reference = new XElement("Reference", new XAttribute("Include", Shared), new XElement("HintPath", "$(MSBuildThisFileDirectory)../manual/Shared.dll"));
            var target = new XElement(
                "Target",
                new XAttribute("Name", "AddFixtureReference"),
                new XAttribute("BeforeTargets", "ResolveAssemblyReferences"),
                new XAttribute("Condition", $"'$(TargetFramework)' == '{TargetFramework}'"),
                new XElement("ItemGroup", reference));
            using var writer = new StreamWriter(archive.CreateEntry($"build/{_prefix}{id}.targets").Open());
            writer.Write(new XDocument(new XElement("Project", target)));
        }

        /// <summary>Finds the synthetic SDK project for the fixture's single restored graph.</summary>
        /// <returns>The evaluation project path.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string GetEvaluationProjectPath() => Directory.GetFiles(Path.Combine(_directory.Path, ApiDirectory, "restore"), "documentation.csproj", SearchOption.AllDirectories)[0];

        /// <summary>Sets the documentation roots and exclusions.</summary>
        /// <param name="roots">Root package names.</param>
        /// <param name="excluded">Excluded package names.</param>
        public void Manifest(string[] roots, string[] excluded)
        {
            using var stream = File.Create(Path.Combine(_directory.Path, "nuget-packages.json"));
            using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            writer.WriteStartArray("additionalPackages");
            foreach (var root in roots)
            {
                writer.WriteStartObject();
                writer.WriteString("id", _prefix + root);
                writer.WriteString("version", RootVersion);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("excludePackages");
            foreach (var id in excluded)
            {
                writer.WriteStringValue(_prefix + id);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("tfmPreference");
            writer.WriteStringValue(TargetFramework);
            writer.WriteEndArray();
            writer.WriteStartObject("dependencyPins");
            foreach (var pin in DependencyPins)
            {
                writer.WriteString(_prefix + pin.Key, pin.Value);
            }

            writer.WriteEndObject();
            writer.WriteStartObject("tfmOverrides");
            foreach (var tfm in TfmOverrides)
            {
                writer.WriteString(_prefix + tfm.Key, tfm.Value);
            }

            writer.WriteEndObject();
            if (RuntimeIdentifier is not null)
            {
                writer.WriteString("runtimeIdentifier", RuntimeIdentifier);
            }

            writer.WriteEndObject();
        }

        /// <summary>Discovers documentation inputs from the feed.</summary>
        /// <returns>The selected assembly groups.</returns>
        public async Task<List<AssemblyGroup>> DiscoverAsync()
        {
            using var fetcher = DecorateFetcher ? new NuGetFetcher() : null;
            var selectedFetcher = fetcher is null ? null : new DelegatingFetcher(fetcher);
            var apiPath = Path.Combine(_directory.Path, ApiDirectory);
            if (UseRelativeApiPath)
            {
                apiPath = Path.GetRelativePath(Environment.CurrentDirectory, apiPath);
            }

            using var source = new NuGetAssemblySource(_directory.Path, apiPath, null, selectedFetcher);
            List<AssemblyGroup> groups = [];
            await foreach (var group in source.DiscoverAsync())
            {
                groups.Add(group);
            }

            return groups;
        }

        /// <summary>Restores an ordinary SDK project against the same local package feed.</summary>
        /// <param name="root">The root package name.</param>
        /// <returns>The SDK project's assets document.</returns>
        /// <exception cref="InvalidOperationException">The SDK restore process cannot complete successfully.</exception>
        public async Task<JsonDocument> RestoreProjectAsync(string root)
        {
            var projectPath = Path.Combine(_directory.Path, "comparison.csproj");
            var project = new XElement(
                "Project",
                new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement(nameof(TargetFramework), TargetFramework)),
                new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", _prefix + root), new XAttribute(nameof(Version), RootVersion ?? "*"))));
            new XDocument(project).Save(projectPath);
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = _directory.Path, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, };
            start.ArgumentList.Add("restore");
            start.ArgumentList.Add(projectPath);
            start.ArgumentList.Add("--configfile");
            start.ArgumentList.Add(Path.Combine(_directory.Path, NuGetConfigFile));
            start.ArgumentList.Add("--verbosity");
            start.ArgumentList.Add("quiet");
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the SDK restore comparison.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
#if NET11_0_OR_GREATER
            var status = await process.WaitForExitStatusAsync();
            var success = status is { Signal: null, ExitCode: 0 };
#else
            await process.WaitForExitAsync();
            var success = process.ExitCode is 0;
#endif
            var output = await stdout;
            var errors = await stderr;
            if (!success)
            {
                throw new InvalidOperationException($"SDK restore failed: {output}{errors}");
            }

            await using var stream = File.OpenRead(Path.Combine(_directory.Path, "obj", "project.assets.json"));
            return await JsonDocument.ParseAsync(stream);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose() => _directory.Dispose();
    }

    /// <summary>Wraps package acquisition without changing its behavior.</summary>
    /// <param name="inner">The underlying package fetcher.</param>
    private sealed class DelegatingFetcher(INuGetFetcher inner) : INuGetFetcher
    {
        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task FetchPackagesAsync(string rootDirectory, string apiPath) => inner.FetchPackagesAsync(rootDirectory, apiPath);

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task FetchPackagesAsync(string rootDirectory, string apiPath, ILogger? logger) => inner.FetchPackagesAsync(rootDirectory, apiPath, logger);

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task FetchPackagesAsync(string rootDirectory, string apiPath, ILogger? logger, CancellationToken cancellationToken) =>
            inner.FetchPackagesAsync(rootDirectory, apiPath, logger, cancellationToken);
    }
}
