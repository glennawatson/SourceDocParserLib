// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Tfm;

namespace SourceDocParser.Tests;

/// <summary>
/// Tests for <see cref="TfmResolver"/> -- focuses on the
/// <c>FindBestRefsTfm</c> path that's backed by NuGet.Frameworks'
/// <c>FrameworkReducer</c>, plus <c>GetPlatformLabel</c>.
/// </summary>
public class TfmResolverTests
{
    /// <summary>
    /// Exact lib/ TFM in refs/ wins.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FindBestRefsTfmReturnsExactMatch()
    {
        var refs = new List<string> { "net8.0", "net9.0", "net10.0" };

        var result = TfmResolver.FindBestRefsTfm("net10.0", refs);

        await Assert.That(result).IsEqualTo("net10.0");
    }

    /// <summary>
    /// Platform-suffixed lib/ TFM falls back to its base TFM in refs/ via
    /// the proper NuGet compatibility rules (this was buggy under the old
    /// string-prefix matcher -- net10.0-android36.0 would not match net10.0
    /// without the dash hack).
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FindBestRefsTfmHandlesPlatformSuffix()
    {
        var refs = new List<string> { "net8.0", "net9.0", "net10.0" };

        var result = TfmResolver.FindBestRefsTfm("net10.0-android36.0", refs);

        await Assert.That(result).IsEqualTo("net10.0");
    }

    /// <summary>
    /// netstandard lib/ falls back to a modern .NET refs/ entry when no
    /// netstandard is present in refs/.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FindBestRefsTfmFallsBackFromNetstandardToModernNet()
    {
        var refs = new List<string> { "net8.0", "net9.0", "net10.0" };

        var result = TfmResolver.FindBestRefsTfm("netstandard2.0", refs);

        await Assert.That(result).IsNotNull();
        await Assert.That(refs.Contains(result!)).IsTrue();
    }

    /// <summary>
    /// .NET Framework lib/ TFM picks a .NET Framework refs/ entry, not a
    /// modern .NET one.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FindBestRefsTfmPicksFrameworkRefsForFrameworkLib()
    {
        var refs = new List<string> { "net462", "net48", "net10.0" };

        var result = TfmResolver.FindBestRefsTfm("net48", refs);

        await Assert.That(result).IsEqualTo("net48");
    }

    /// <summary>
    /// Empty refs/ list returns null without parsing anything.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FindBestRefsTfmReturnsNullWhenNoRefs()
    {
        var result = TfmResolver.FindBestRefsTfm("net10.0", []);

        await Assert.That(result).IsNull();
    }

    /// <summary>
    /// Modern platform-suffixed TFMs report their platform label.
    /// </summary>
    /// <param name="tfm">TFM under test.</param>
    /// <param name="expected">Expected platform label.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("net10.0-android36.0", "android")]
    [Arguments("net10.0-ios18.0", "ios")]
    [Arguments("net10.0-maccatalyst18.0", "maccatalyst")]
    [Arguments("net10.0-windows10.0.19041.0", "windows")]
    public async Task GetPlatformLabelHandlesModernSuffixes(string tfm, string expected)
    {
        var label = TfmResolver.GetPlatformLabel(tfm);

        await Assert.That(label).IsEqualTo(expected);
    }

    /// <summary>
    /// Legacy Xamarin / mono / UAP TFMs report their platform label.
    /// </summary>
    /// <param name="tfm">TFM under test.</param>
    /// <param name="expected">Expected platform label.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("monoandroid12.0", "android")]
    [Arguments("xamarinios10", "ios")]
    [Arguments("xamarinmac20", "maccatalyst")]
    [Arguments("uap10.0", "windows")]
    public async Task GetPlatformLabelHandlesLegacyMonikers(string tfm, string expected)
    {
        var label = TfmResolver.GetPlatformLabel(tfm);

        await Assert.That(label).IsNotNull();
        await Assert.That(label).IsEqualTo(expected);
    }

    /// <summary>
    /// Plain TFMs without a platform suffix return null.
    /// </summary>
    /// <param name="tfm">TFM under test.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("net10.0")]
    [Arguments("net8.0")]
    [Arguments("net48")]
    [Arguments("netstandard2.0")]
    public async Task GetPlatformLabelReturnsNullForPlatformNeutral(string tfm)
    {
        var label = TfmResolver.GetPlatformLabel(tfm);

        await Assert.That(label).IsNull();
    }

    /// <summary>
    /// SelectTfm: an exact override match wins over the preference list,
    /// even when the preference list also contains a candidate.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectTfmHonoursExactOverride()
    {
        var available = new List<string> { "net8.0", "net9.0", "net10.0" };

        var result = TfmResolver.SelectTfm(available, "net8.0", ["net10.0"]);

        await Assert.That(result).IsEqualTo("net8.0");
    }

    /// <summary>SelectTfm: a prefix override (<c>net8</c>) matches <c>net8.0</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectTfmHonoursPrefixOverride()
    {
        var available = new List<string> { "net8.0", "net9.0" };

        var result = TfmResolver.SelectTfm(available, "net8", ["net9.0"]);

        await Assert.That(result).IsEqualTo("net8.0");
    }

    /// <summary>SelectTfm: walks the preference list in order, returning the first available exact match.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectTfmWalksPreferenceListInOrder()
    {
        var available = new List<string> { "net8.0", "net10.0" };

        var result = TfmResolver.SelectTfm(available, tfmOverride: null, tfmPreference: ["net10.0", "net8.0"]);

        await Assert.That(result).IsEqualTo("net10.0");
    }

    /// <summary>SelectTfm: preference prefix (<c>net8</c>) matches <c>net8.0</c> when no exact match exists.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectTfmFallsBackToPreferencePrefix()
    {
        var available = new List<string> { "net8.0" };

        var result = TfmResolver.SelectTfm(available, tfmOverride: null, tfmPreference: ["net8"]);

        await Assert.That(result).IsEqualTo("net8.0");
    }

    /// <summary>SelectTfm: with no exact / prefix / major-version match, falls back to the highest netstandard available.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectTfmFallsBackToHighestNetstandard()
    {
        var available = new List<string> { "netstandard2.0", "netstandard2.1" };

        var result = TfmResolver.SelectTfm(available, tfmOverride: null, tfmPreference: ["net10.0"]);

        await Assert.That(result).IsEqualTo("netstandard2.1");
    }

    /// <summary>SelectTfm: returns null when no preference match and no netstandard fallback is available.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectTfmReturnsNullWhenNothingMatches()
    {
        var available = new List<string> { "monoandroid12.0" };

        var result = TfmResolver.SelectTfm(available, tfmOverride: null, tfmPreference: ["net10.0"]);

        await Assert.That(result).IsNull();
    }

    /// <summary>SelectTfm: an unmatched override falls through to the preference list (override is a priority hint, not a hard pin).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectTfmUnmatchedOverrideFallsThroughToPreference()
    {
        var available = new List<string> { "net8.0" };

        var result = TfmResolver.SelectTfm(available, "net6.0", ["net8.0"]);

        await Assert.That(result).IsEqualTo("net8.0");
    }

    /// <summary>SelectAllSupportedTfms: with no override, every TFM matching any preference (exact, prefix, or major version) is returned.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectAllSupportedTfmsCollectsAllPreferenceMatches()
    {
        var available = new List<string> { "net8.0", "net9.0", "net10.0", "monoandroid12.0" };

        var result = TfmResolver.SelectAllSupportedTfms(available, tfmOverride: null, tfmPreference: ["net8.0", "net10.0"]);

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result).Contains("net8.0");
        await Assert.That(result).Contains("net10.0");
    }

    /// <summary>SelectAllSupportedTfms: an override pins the result to whichever single TFM SelectTfm picks.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectAllSupportedTfmsHonoursOverride()
    {
        var available = new List<string> { "net8.0", "net9.0" };

        var result = TfmResolver.SelectAllSupportedTfms(available, "net9.0", ["net8.0"]);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0]).IsEqualTo("net9.0");
    }

    /// <summary>SelectAllSupportedTfms: when no preference matches, falls back to every available netstandard variant (not just the highest).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectAllSupportedTfmsCollectsAllNetstandardOnFallback()
    {
        var available = new List<string> { "netstandard2.0", "netstandard2.1" };

        var result = TfmResolver.SelectAllSupportedTfms(available, tfmOverride: null, tfmPreference: ["net10.0"]);

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result).Contains("netstandard2.0");
        await Assert.That(result).Contains("netstandard2.1");
    }

    /// <summary>SelectAllSupportedTfms: an unmatched override pins the result to whatever the SelectTfm fallback picks (here the preference match).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectAllSupportedTfmsUnmatchedOverridePinsToPreferenceFallback()
    {
        var available = new List<string> { "net8.0" };

        var result = TfmResolver.SelectAllSupportedTfms(available, "net6.0", ["net8.0"]);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0]).IsEqualTo("net8.0");
    }

    /// <summary>SelectCompatibleTfms: a modern .NET target picks up its own TFM plus every lower-version compatible bucket (incl. netstandard).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectCompatibleTfmsIncludesLowerVersionsAndNetstandard()
    {
        var available = new List<string> { "net8.0", "net6.0", "netstandard2.0", "netstandard2.1", "net48" };

        var result = TfmResolver.SelectCompatibleTfms("net8.0", available);

        await Assert.That(result).Contains("net8.0");
        await Assert.That(result).Contains("net6.0");
        await Assert.That(result).Contains("netstandard2.0");
        await Assert.That(result).Contains("netstandard2.1");
        await Assert.That(result).DoesNotContain("net48");
    }

    /// <summary>SelectCompatibleTfms: results are ordered by descending rank so the target's own bucket comes first.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectCompatibleTfmsOrdersHighestRankFirst()
    {
        var available = new List<string> { "netstandard2.0", "net6.0", "net8.0" };

        var result = TfmResolver.SelectCompatibleTfms("net8.0", available);

        await Assert.That(result[0]).IsEqualTo("net8.0");
        await Assert.That(result[1]).IsEqualTo("net6.0");
        await Assert.That(result[2]).IsEqualTo("netstandard2.0");
    }

    /// <summary>SelectCompatibleTfms: a netstandard2.0 target excludes net8.0 (modern .NET libs aren't compatible with a netstandard consumer).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectCompatibleTfmsExcludesHigherTargetFrameworksWhenTargetIsNetstandard()
    {
        var available = new List<string> { "netstandard2.0", "netstandard1.6", "net8.0" };

        var result = TfmResolver.SelectCompatibleTfms("netstandard2.0", available);

        await Assert.That(result).Contains("netstandard2.0");
        await Assert.That(result).Contains("netstandard1.6");
        await Assert.That(result).DoesNotContain("net8.0");
    }

    /// <summary>SelectCompatibleTfms: returns empty when nothing in availableTfms is reachable from the target.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectCompatibleTfmsReturnsEmptyWhenNothingMatches()
    {
        var available = new List<string> { "net48", "net472" };

        var result = TfmResolver.SelectCompatibleTfms("netstandard2.0", available);

        await Assert.That(result.Count).IsEqualTo(0);
    }

    /// <summary>SelectCompatibleTfms: short-circuits cheaply on empty input.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectCompatibleTfmsReturnsEmptyForEmptyInput()
    {
        var result = TfmResolver.SelectCompatibleTfms("net8.0", []);

        await Assert.That(result.Count).IsEqualTo(0);
    }

    /// <summary>SelectCompatibleTfms: validates input and rejects null/whitespace target TFM.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectCompatibleTfmsRejectsBlankTarget()
    {
        await Assert.That(() => TfmResolver.SelectCompatibleTfms(string.Empty, ["net8.0"])).Throws<ArgumentException>();
    }

    /// <summary>
    /// FindBestRefsTfm: a non-netstandard lib TFM that the reducer cannot pair
    /// with any refs entry returns null (covers the non-netstandard branch of
    /// the reducer's null-result fallback in <c>FindBestRefsTfmSlow</c>).
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FindBestRefsTfmReturnsNullForLegacyLibAgainstModernRefs()
    {
        var refs = new List<string> { "net8.0", "net10.0" };

        var result = TfmResolver.FindBestRefsTfm("monoandroid12.0", refs);

        await Assert.That(result).IsNull();
    }

    /// <summary>
    /// HasOnlyLegacyTfms: a Xamarin / MonoAndroid / .NET-Framework-pre-5
    /// only package is classified legacy so the fetcher can drop the
    /// "no supported TFM" warning to information.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task HasOnlyLegacyTfmsDetectsXamarinAndMonoOnlyPackages()
    {
        IReadOnlyList<string> legacy = ["MonoAndroid10", "MonoTouch10", "xamarinios10", "xamarinmac20", "xamarintvos10", "xamarinwatchos10", "net461"];

        var result = TfmResolver.HasOnlyLegacyTfms(legacy);

        await Assert.That(result).IsTrue();
    }

    /// <summary>
    /// HasOnlyLegacyTfms: silverlight / windows-phone / portable-* / win8 /
    /// uap legacy TFMs all classify as legacy.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task HasOnlyLegacyTfmsDetectsSilverlightWindowsPhoneAndPortableOnlyPackages()
    {
        IReadOnlyList<string> legacy = ["sl5", "wp8", "wpa81", "win8", "portable-net45+win8+wp8+wpa81", "uap10.0"];

        var result = TfmResolver.HasOnlyLegacyTfms(legacy);

        await Assert.That(result).IsTrue();
    }

    /// <summary>HasOnlyLegacyTfms: a netstandard variant alone is enough to mark the package as non-legacy.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task HasOnlyLegacyTfmsReturnsFalseWhenAnyNetstandardIsPresent()
    {
        IReadOnlyList<string> mixed = ["MonoAndroid10", "xamarinios10", "netstandard2.0"];

        var result = TfmResolver.HasOnlyLegacyTfms(mixed);

        await Assert.That(result).IsFalse();
    }

    /// <summary>HasOnlyLegacyTfms: any modern .NET (net5+) variant is enough to mark the package as non-legacy.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task HasOnlyLegacyTfmsReturnsFalseWhenAnyModernNetIsPresent()
    {
        IReadOnlyList<string> mixed = ["MonoAndroid10", "xamarinios10", "net8.0"];

        var result = TfmResolver.HasOnlyLegacyTfms(mixed);

        await Assert.That(result).IsFalse();
    }

    /// <summary>HasOnlyLegacyTfms: returns false on an empty list (nothing to classify).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task HasOnlyLegacyTfmsReturnsFalseForEmptyInput()
    {
        IReadOnlyList<string> empty = [];

        var result = TfmResolver.HasOnlyLegacyTfms(empty);

        await Assert.That(result).IsFalse();
    }

    /// <summary>HasOnlyLegacyTfms: rejects null input via the standard guard.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task HasOnlyLegacyTfmsRejectsNull() =>
        await Assert.That(() => TfmResolver.HasOnlyLegacyTfms(null!)).Throws<ArgumentNullException>();

    /// <summary>
    /// HasOnlyLegacyTfms: net462+ counts as supported (it implements
    /// netstandard 2.0 type forwards). A package shipping only net462
    /// is therefore NOT legacy.
    /// </summary>
    /// <param name="supportedFrameworkTfm">Supported .NET Framework variant under test.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("net462")]
    [Arguments("net47")]
    [Arguments("net471")]
    [Arguments("net472")]
    [Arguments("net48")]
    [Arguments("net481")]
    public async Task HasOnlyLegacyTfmsTreatsNet462AndNewerAsSupported(string supportedFrameworkTfm)
    {
        IReadOnlyList<string> tfms = [supportedFrameworkTfm];

        var result = TfmResolver.HasOnlyLegacyTfms(tfms);

        await Assert.That(result).IsFalse();
    }

    /// <summary>
    /// HasOnlyLegacyTfms: pre-net462 variants (net20, net35, net40,
    /// net45, net451, net46, net461) ship without netstandard 2.0
    /// support and are correctly classified as legacy.
    /// </summary>
    /// <param name="legacyFrameworkTfm">Pre-net462 variant under test.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("net20")]
    [Arguments("net35")]
    [Arguments("net40")]
    [Arguments("net45")]
    [Arguments("net451")]
    [Arguments("net46")]
    [Arguments("net461")]
    public async Task HasOnlyLegacyTfmsTreatsPreNet462AsLegacy(string legacyFrameworkTfm)
    {
        IReadOnlyList<string> tfms = [legacyFrameworkTfm];

        var result = TfmResolver.HasOnlyLegacyTfms(tfms);

        await Assert.That(result).IsTrue();
    }

    /// <summary>IsLegacyDotNetFramework: rejects null input via the standard guard.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsLegacyDotNetFrameworkRejectsNull() =>
        await Assert.That(() => TfmResolver.IsLegacyDotNetFramework(null!)).Throws<ArgumentNullException>();
}
