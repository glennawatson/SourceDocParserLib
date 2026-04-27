// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.NuGet.Models;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins the primary-package filtering helpers on
/// <see cref="NuGetAssemblySource"/> — the change that stops the
/// walker from emitting documentation pages for transitively-pulled
/// runtime/platform assemblies (Microsoft.MAUI.Controls, Xamarin.*,
/// AndroidX.*, Kotlin.*) that came in via a primary's nuspec deps.
/// </summary>
public class PrimaryPrefixFilterTests
{
    /// <summary>
    /// The prefix array lays out IDs as bare-id / id+dot pairs so
    /// the filter handles umbrella DLL exact matches alongside
    /// sibling prefix matches in a single pass.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildPrimaryPrefixesEmitsBareAndDottedPair()
    {
        var config = new PackageConfig(
            NugetPackageOwners: [],
            TfmPreference: [],
            AdditionalPackages: [new("Splat", null), new("ReactiveUI", null)],
            ExcludePackages: [],
            ExcludePackagePrefixes: [],
            ReferencePackages: [],
            TfmOverrides: []);

        var prefixes = NuGetAssemblySource.BuildPrimaryPrefixes(config);

        await Assert.That(prefixes).IsEquivalentTo((string[])["Splat", "Splat.", "ReactiveUI", "ReactiveUI."]);
    }

    /// <summary>
    /// An empty <c>additionalPackages</c> list returns an empty
    /// prefix array — which <see cref="NuGetAssemblySource.IsPrimaryDll"/>
    /// reads as "no filter, walk everything" so owner-discovery and
    /// no-config flows aren't broken.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildPrimaryPrefixesReturnsEmptyForNoAdditionals()
    {
        var config = new PackageConfig(
            NugetPackageOwners: [],
            TfmPreference: [],
            AdditionalPackages: [],
            ExcludePackages: [],
            ExcludePackagePrefixes: [],
            ReferencePackages: [],
            TfmOverrides: []);

        var prefixes = NuGetAssemblySource.BuildPrimaryPrefixes(config);

        await Assert.That(prefixes.Length).IsEqualTo(0);
    }

    /// <summary>
    /// Bare-ID exact match: the umbrella DLL (<c>Splat.dll</c>)
    /// matches the bare <c>Splat</c> entry — case-insensitive.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsPrimaryDllMatchesBareIdExactly()
    {
        string[] prefixes = ["Splat", "Splat.", "ReactiveUI", "ReactiveUI."];

        await Assert.That(NuGetAssemblySource.IsPrimaryDll("Splat", prefixes)).IsTrue();
        await Assert.That(NuGetAssemblySource.IsPrimaryDll("splat", prefixes)).IsTrue();
        await Assert.That(NuGetAssemblySource.IsPrimaryDll("ReactiveUI", prefixes)).IsTrue();
    }

    /// <summary>
    /// Sibling assemblies (<c>Splat.Core.dll</c>, <c>Splat.Logging.dll</c>)
    /// match the dotted prefix entry — that's how the umbrella's
    /// <c>[TypeForwardedTo]</c> targets get their own pages.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsPrimaryDllMatchesDottedSiblings()
    {
        string[] prefixes = ["Splat", "Splat.", "ReactiveUI", "ReactiveUI."];

        await Assert.That(NuGetAssemblySource.IsPrimaryDll("Splat.Core", prefixes)).IsTrue();
        await Assert.That(NuGetAssemblySource.IsPrimaryDll("Splat.Logging", prefixes)).IsTrue();
        await Assert.That(NuGetAssemblySource.IsPrimaryDll("ReactiveUI.AndroidX.UiToolkit", prefixes)).IsTrue();
    }

    /// <summary>
    /// Transitive DLLs that don't share a primary's prefix
    /// (<c>Microsoft.Maui.Controls.dll</c>, <c>Xamarin.Google.*</c>,
    /// <c>System.Reactive.dll</c>) get filtered out — they stay on
    /// disk for the compilation's fallback index but the walker
    /// never visits them.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsPrimaryDllRejectsUnrelatedDlls()
    {
        string[] prefixes = ["Splat", "Splat.", "ReactiveUI", "ReactiveUI."];

        await Assert.That(NuGetAssemblySource.IsPrimaryDll("Microsoft.Maui.Controls", prefixes)).IsFalse();
        await Assert.That(NuGetAssemblySource.IsPrimaryDll("Xamarin.Google.Crypto.Tink", prefixes)).IsFalse();
        await Assert.That(NuGetAssemblySource.IsPrimaryDll("System.Reactive", prefixes)).IsFalse();
        await Assert.That(NuGetAssemblySource.IsPrimaryDll("AndroidX.Core", prefixes)).IsFalse();
    }

    /// <summary>
    /// An empty prefix array means "no filter configured" — every
    /// DLL passes. Mirrors the behaviour of the source when the
    /// config has no <c>additionalPackages</c>.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsPrimaryDllReturnsTrueWhenNoPrefixes()
    {
        await Assert.That(NuGetAssemblySource.IsPrimaryDll("Anything", [])).IsTrue();
        await Assert.That(NuGetAssemblySource.IsPrimaryDll("Microsoft.Maui.Controls", [])).IsTrue();
    }

    /// <summary>
    /// A near-miss (<c>Splat</c> is primary, <c>SplatExtra</c> is
    /// some unrelated package) doesn't accidentally pass: the
    /// dotted prefix is required for sibling matching, not just
    /// "starts with".
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsPrimaryDllRejectsNearMissNames()
    {
        string[] prefixes = ["Splat", "Splat."];

        await Assert.That(NuGetAssemblySource.IsPrimaryDll("SplatExtra", prefixes)).IsFalse();
        await Assert.That(NuGetAssemblySource.IsPrimaryDll("SplatLike", prefixes)).IsFalse();
    }
}
