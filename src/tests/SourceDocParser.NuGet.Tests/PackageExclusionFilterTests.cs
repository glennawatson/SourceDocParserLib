// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.NuGet.Infrastructure;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins <see cref="PackageExclusionFilter"/>: the user-driven exclude
/// list (exact ids + prefix list) and the built-in default-skip rules
/// for native runtime packages and Microsoft platform shim packages.
/// Tested in isolation so a regression in either rule fails on its
/// own line.
/// </summary>
public class PackageExclusionFilterTests
{
    /// <summary>Exact-match exclude IDs are case-insensitive.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExcludedByUserMatchesExactIdsCaseInsensitive()
    {
        await Assert.That(PackageExclusionFilter.IsExcludedByUser("Foo.Bar", ["FOO.BAR"], [])).IsTrue();
        await Assert.That(PackageExclusionFilter.IsExcludedByUser("foo.bar", ["Foo.Bar"], [])).IsTrue();
    }

    /// <summary>Prefix excludes match any id starting with the prefix.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExcludedByUserMatchesPrefixes()
    {
        await Assert.That(PackageExclusionFilter.IsExcludedByUser("My.Sdk.Tools", [], ["My.Sdk."])).IsTrue();
        await Assert.That(PackageExclusionFilter.IsExcludedByUser("Different", [], ["My.Sdk."])).IsFalse();
    }

    /// <summary>Empty exclude lists never match.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExcludedByUserReturnsFalseForEmptyLists() => await Assert.That(PackageExclusionFilter.IsExcludedByUser("Anything", [], [])).IsFalse();

    /// <summary>Native runtime.* packages are always default-transitive-skipped.</summary>
    /// <param name="id">Native runtime package ID.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("runtime.linux-x64.System.Foo")]
    [Arguments("RUNTIME.WIN-X64.SOMETHING")]
    [Arguments("runtime.osx.runtime.native.System")]
    public async Task DefaultTransitiveSkipsRuntimePackages(string id) =>
        await Assert.That(PackageExclusionFilter.IsDefaultTransitiveSkip(id)).IsTrue();

    /// <summary>Microsoft.NET.Native.* packages are default-transitive-skipped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DefaultTransitiveSkipsMicrosoftNetNative() => await Assert.That(PackageExclusionFilter.IsDefaultTransitiveSkip("Microsoft.NET.Native.Compiler")).IsTrue();

    /// <summary>Microsoft.NETCore.* shim families are default-transitive-skipped.</summary>
    /// <param name="id">Microsoft.NETCore subfamily package id.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("Microsoft.NETCore.Native.Compiler")]
    [Arguments("Microsoft.NETCore.UniversalWindowsPlatform")]
    [Arguments("Microsoft.NETCore.Targets")]
    [Arguments("Microsoft.NETCore.Platforms")]
    [Arguments("Microsoft.NETCore.Jit")]
    [Arguments("Microsoft.NETCore.Runtime.CoreCLR")]
    [Arguments("Microsoft.NETCore.Portable.Compatibility")]
    public async Task DefaultTransitiveSkipsMicrosoftNetCoreShimFamilies(string id) =>
        await Assert.That(PackageExclusionFilter.IsDefaultTransitiveSkip(id)).IsTrue();

    /// <summary>Regular Microsoft.* packages outside the shim families pass through.</summary>
    /// <param name="id">Regular Microsoft package id.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("Microsoft.Extensions.Logging")]
    [Arguments("Microsoft.AspNetCore.App")]
    [Arguments("Microsoft.NET.Sdk")]
    public async Task DefaultTransitiveAllowsRegularMicrosoftPackages(string id) =>
        await Assert.That(PackageExclusionFilter.IsDefaultTransitiveSkip(id)).IsFalse();

    /// <summary>User packages that don't start with <c>Microsoft</c> or <c>runtime.</c> pass through.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DefaultTransitiveAllowsUserPackages()
    {
        await Assert.That(PackageExclusionFilter.IsDefaultTransitiveSkip("ReactiveUI")).IsFalse();
        await Assert.That(PackageExclusionFilter.IsDefaultTransitiveSkip("My.Custom.Package")).IsFalse();
    }

    /// <summary>Empty package id returns false rather than throwing.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DefaultTransitiveSkipReturnsFalseForEmpty() => await Assert.That(PackageExclusionFilter.IsDefaultTransitiveSkip(string.Empty)).IsFalse();
}
