// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.LibCompilation;

namespace SourceDocParser.Tests;

/// <summary>
/// Pins <see cref="UnresolvableReferenceFilter"/> -- the gatekeeper
/// that decides which assembly references the resolver should
/// silently skip rather than log as <c>Unable to resolve assembly reference</c>.
/// Both the version-stub heuristic and the platform/
/// SDK name list are exercised here through their primitive entry
/// points so the tests don't depend on ICSharpCode's
/// <c>AssemblyReference</c> constructor shape.
/// </summary>
public class UnresolvableReferenceFilterTests
{
    /// <summary>The 0.0.0.0 sentinel (ECMA-335 stub) is filtered.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsStubVersionMatchesAllZeroVersion() =>
        await Assert.That(UnresolvableReferenceFilter.IsStubVersion(new(0, 0, 0, 0))).IsTrue();

    /// <summary>The 255.255.255.255 sentinel (Uno design-time projection) is filtered.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsStubVersionMatchesAllMaxVersion() =>
        await Assert.That(UnresolvableReferenceFilter.IsStubVersion(new(255, 255, 255, 255))).IsTrue();

    /// <summary>A real-world version (e.g. System.Reactive 6.0) is NOT a stub.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsStubVersionRejectsRealisticVersions()
    {
        await Assert.That(UnresolvableReferenceFilter.IsStubVersion(new(6, 0, 0, 0))).IsFalse();
        await Assert.That(UnresolvableReferenceFilter.IsStubVersion(new(1, 2, 3, 4))).IsFalse();
        await Assert.That(UnresolvableReferenceFilter.IsStubVersion(new(255, 0, 0, 0))).IsFalse();
        await Assert.That(UnresolvableReferenceFilter.IsStubVersion(new(0, 0, 0, 1))).IsFalse();
    }

    /// <summary>Null version is treated as not a stub.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsStubVersionRejectsNull() =>
        await Assert.That(UnresolvableReferenceFilter.IsStubVersion(null)).IsFalse();

    /// <summary>Exact-match platform refs are filtered.</summary>
    /// <param name="name">Assembly name to test.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("Java.Interop")]
    [Arguments("Microsoft.iOS")]
    [Arguments("Microsoft.macOS")]
    [Arguments("Microsoft.MacCatalyst")]
    [Arguments("PresentationCore")]
    [Arguments("PresentationFramework")]
    [Arguments("System.Xaml")]
    [Arguments("System.Windows.Forms")]
    [Arguments("Microsoft.WinUI")]
    [Arguments("WinRT.Runtime")]
    [Arguments("Microsoft.Windows.SDK.NET")]
    [Arguments("Microsoft.InteractiveExperiences.Projection")]
    [Arguments("Microsoft.Web.WebView2.Core.Projection")]
    [Arguments("SMDiagnostics")]
    [Arguments("System.ServiceModel.Internals")]
    [Arguments("Uno")]
    [Arguments("Uno.UI")]
    [Arguments("Uno.UI.Toolkit")]
    [Arguments("_Microsoft.Android.Resource.Designer")]
    [Arguments("Windows.Foundation.UniversalApiContract")]
    public async Task IsKnownUnresolvableNameFiltersExactMatchSet(string name) =>
        await Assert.That(UnresolvableReferenceFilter.IsKnownUnresolvableName(name)).IsTrue();

    /// <summary>Prefix-match families are filtered.</summary>
    /// <param name="name">Assembly name to test.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("Microsoft.Android.Sdk.Loader")]
    [Arguments("_Microsoft.Android.Foo.Bar")]
    [Arguments("Xamarin.Google.Guava.ListenableFuture")]
    [Arguments("Xamarin.Android.Resource.Designer")]
    [Arguments("Microsoft.AspNetCore.Components")]
    [Arguments("Microsoft.AspNetCore.Components.Web")]
    [Arguments("Microsoft.Maui.Controls")]
    [Arguments("Microsoft.Maui.Essentials")]
    public async Task IsKnownUnresolvableNameFiltersPrefixFamilies(string name) =>
        await Assert.That(UnresolvableReferenceFilter.IsKnownUnresolvableName(name)).IsTrue();

    /// <summary>
    /// Real-world packages that DO ship as NuGet must NOT be filtered
    /// here -- otherwise the resolver would silently lose them. Pins
    /// the negative case for the things we actually want resolved.
    /// </summary>
    /// <param name="name">Assembly name to test.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("System.Reactive")]
    [Arguments("Splat")]
    [Arguments("Splat.Core")]
    [Arguments("Newtonsoft.Json")]
    [Arguments("DryIoc")]
    [Arguments("DynamicData")]
    [Arguments("Avalonia.Base")]
    [Arguments("Avalonia.Controls")]
    [Arguments("ReactiveUI")]
    [Arguments("Akavache")]
    [Arguments("System.Text.Json")]
    [Arguments("Microsoft.Extensions.DependencyInjection.Abstractions")]
    public async Task IsKnownUnresolvableNamePassesNuGetPackages(string name) =>
        await Assert.That(UnresolvableReferenceFilter.IsKnownUnresolvableName(name)).IsFalse();

    /// <summary>Empty name throws -- the filter is only meant to take real refs.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsKnownUnresolvableNameRejectsEmpty() =>
        await Assert.That(() => UnresolvableReferenceFilter.IsKnownUnresolvableName(string.Empty)).Throws<ArgumentException>();
}
