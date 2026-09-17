// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.NuGet.Infrastructure;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins <see cref="KnownFrameworkPackageMap"/> -- the lookup that
/// turns synthetic framework / projection assembly names back into
/// the NuGet package IDs that actually ship them. Drives the
/// "auto-add transitive deps for WinUI / WebView2 surfaces" path
/// in the fetcher.
/// </summary>
public class KnownFrameworkPackageMapTests
{
    /// <summary>Fixture value for MicrosoftWindowsAppSDK.</summary>
    private const string MicrosoftWindowsAppSDK = "Microsoft.WindowsAppSDK";

    /// <summary>Fixture value for MicrosoftWinUI.</summary>
    private const string MicrosoftWinUI = "Microsoft.WinUI";

    /// <summary>Expected fixture value used by AdditionalNuGetPackagesForPreservesFirstSeenOrder.</summary>
    private const int AdditionalNuGetPackagesForPreservesFirstSeenOrderExpectedValue = 2;

    /// <summary>Every WinUI / Windows App SDK projection collapses onto Microsoft.WindowsAppSDK.</summary>
    /// <param name="referenceName">Synthetic projection assembly name.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments(MicrosoftWinUI)]
    [Arguments("WinRT.Runtime")]
    [Arguments("Microsoft.Windows.SDK.NET")]
    [Arguments("Microsoft.InteractiveExperiences.Projection")]
    [Arguments("Microsoft.Web.WebView2.Core.Projection")]
    [Arguments("Microsoft.Windows.UI.Xaml")]
    [Arguments("Microsoft.Windows.AppLifecycle.Projection")]
    public async Task WinUiProjectionsMapToWindowsAppSdk(string referenceName) =>
        await Assert.That(KnownFrameworkPackageMap.TryGetPackageId(referenceName)).IsEqualTo(MicrosoftWindowsAppSDK);

    /// <summary>Standalone WebView2 surfaces map to Microsoft.Web.WebView2.</summary>
    /// <param name="referenceName">WebView2 surface assembly name.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("Microsoft.Web.WebView2.Core")]
    [Arguments("Microsoft.Web.WebView2.Wpf")]
    [Arguments("Microsoft.Web.WebView2.WinForms")]
    public async Task WebView2SurfacesMapToWebView2Package(string referenceName) =>
        await Assert.That(KnownFrameworkPackageMap.TryGetPackageId(referenceName)).IsEqualTo("Microsoft.Web.WebView2");

    /// <summary>Unknown / NuGet-shipped names return null so the caller knows to leave them alone.</summary>
    /// <param name="referenceName">Reference name with no mapping.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("System.Reactive")]
    [Arguments("Splat")]
    [Arguments("DryIoc")]
    [Arguments("Newtonsoft.Json")]
    [Arguments("ReactiveUI")]
    public async Task UnknownNameReturnsNull(string referenceName) =>
        await Assert.That(KnownFrameworkPackageMap.TryGetPackageId(referenceName)).IsNull();

    /// <summary>Empty/whitespace name throws the standard guard exception.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryGetPackageIdRejectsBlankInput() =>
        await Assert.That(static () => KnownFrameworkPackageMap.TryGetPackageId(string.Empty)).Throws<ArgumentException>();

    /// <summary>
    /// AdditionalNuGetPackagesFor de-duplicates: multiple WinUI
    /// projections collapse to a single Microsoft.WindowsAppSDK
    /// entry, in first-seen order.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AdditionalNuGetPackagesForDedupesIntoSinglePackage()
    {
        var packages = KnownFrameworkPackageMap.AdditionalNuGetPackagesFor(
        [
            MicrosoftWinUI,
            "WinRT.Runtime",
            "Microsoft.Windows.SDK.NET",
            "Microsoft.InteractiveExperiences.Projection",
        ]);

        await Assert.That(packages.Count).IsEqualTo(1);
        await Assert.That(packages[0]).IsEqualTo(MicrosoftWindowsAppSDK);
    }

    /// <summary>AdditionalNuGetPackagesFor preserves first-seen ordering when multiple distinct mappings appear in the same input list.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AdditionalNuGetPackagesForPreservesFirstSeenOrder()
    {
        var packages = KnownFrameworkPackageMap.AdditionalNuGetPackagesFor(
        [
            "Microsoft.Web.WebView2.Wpf",
            MicrosoftWinUI,
            "Microsoft.Web.WebView2.Core",
        ]);

        await Assert.That(packages.Count).IsEqualTo(AdditionalNuGetPackagesForPreservesFirstSeenOrderExpectedValue);
        await Assert.That(packages[0]).IsEqualTo("Microsoft.Web.WebView2");
        await Assert.That(packages[1]).IsEqualTo(MicrosoftWindowsAppSDK);
    }

    /// <summary>Unmapped entries are filtered out without affecting the result of mapped ones.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AdditionalNuGetPackagesForIgnoresUnmappedEntries()
    {
        var packages = KnownFrameworkPackageMap.AdditionalNuGetPackagesFor(
        [
            "System.Reactive",
            MicrosoftWinUI,
            "Splat",
        ]);

        await Assert.That(packages.Count).IsEqualTo(1);
        await Assert.That(packages[0]).IsEqualTo(MicrosoftWindowsAppSDK);
    }

    /// <summary>Empty / whitespace entries are silently skipped (not mapped, not thrown).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AdditionalNuGetPackagesForSkipsBlankEntries()
    {
        var packages = KnownFrameworkPackageMap.AdditionalNuGetPackagesFor([string.Empty, MicrosoftWinUI]);

        await Assert.That(packages.Count).IsEqualTo(1);
        await Assert.That(packages[0]).IsEqualTo(MicrosoftWindowsAppSDK);
    }

    /// <summary>Null input is rejected via the standard guard.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AdditionalNuGetPackagesForRejectsNullInput() =>
        await Assert.That(static () => KnownFrameworkPackageMap.AdditionalNuGetPackagesFor(null!)).Throws<ArgumentNullException>();

    /// <summary>
    /// KnownReferenceNames is non-empty and contains both the WinUI
    /// and WebView2 surface keys -- a smoke check that the snapshot
    /// helper actually surfaces the data.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task KnownReferenceNamesIncludesEverySurfaceFamily()
    {
        var names = KnownFrameworkPackageMap.KnownReferenceNames();

        await Assert.That(names.Length).IsGreaterThan(0);
        await Assert.That(names).Contains(MicrosoftWinUI);
        await Assert.That(names).Contains("Microsoft.Web.WebView2.Core");
    }
}
