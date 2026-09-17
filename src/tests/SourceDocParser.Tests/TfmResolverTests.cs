// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
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
    /// <summary>Fixture value for Net80.</summary>
    private const string Net80 = "net8.0";

    /// <summary>Fixture value for Net90.</summary>
    private const string Net90 = "net9.0";

    /// <summary>Fixture value for Net100.</summary>
    private const string Net100 = "net10.0";

    /// <summary>Fixture value for Netstandard20.</summary>
    private const string Netstandard20 = "netstandard2.0";

    /// <summary>Fixture value for Net48.</summary>
    private const string Net48 = "net48";

    /// <summary>Fixture value for Netstandard21.</summary>
    private const string Netstandard21 = "netstandard2.1";

    /// <summary>Fixture value for Monoandroid120.</summary>
    private const string Monoandroid120 = "monoandroid12.0";

    /// <summary>Fixture value for Net60.</summary>
    private const string Net60 = "net6.0";

    /// <summary>Fixture value for MonoAndroid10.</summary>
    private const string MonoAndroid10 = "MonoAndroid10";

    /// <summary>Fixture value for Xamarinios10.</summary>
    private const string Xamarinios10 = "xamarinios10";

    /// <summary>Expected fixture value used by SelectAllSupportedTfmsCollectsAllPreferenceMatches.</summary>
    private const int SelectAllSupportedTfmsCollectsAllPreferenceMatchesExpectedValue = 2;

    /// <summary>Exact lib/ TFM in refs/ wins.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FindBestRefsTfmReturnsExactMatch()
    {
        var refs = new List<string> { Net80, Net90, Net100 };

        var result = TfmResolver.FindBestRefsTfm(Net100, refs);

        await Assert.That(result).IsEqualTo(Net100);
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
        var refs = new List<string> { Net80, Net90, Net100 };

        var result = TfmResolver.FindBestRefsTfm("net10.0-android36.0", refs);

        await Assert.That(result).IsEqualTo(Net100);
    }

    /// <summary>Netstandard lib/ falls back to a modern .NET refs/ entry when no netstandard is present in refs/.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FindBestRefsTfmFallsBackFromNetstandardToModernNet()
    {
        var refs = new List<string> { Net80, Net90, Net100 };

        var result = TfmResolver.FindBestRefsTfm(Netstandard20, refs);

        await Assert.That(result).IsNotNull();
        await Assert.That(refs.Contains(result!)).IsTrue();
    }

    /// <summary>.NET Framework lib/ TFM picks a .NET Framework refs/ entry, not a modern .NET one.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FindBestRefsTfmPicksFrameworkRefsForFrameworkLib()
    {
        var refs = new List<string> { "net462", Net48, Net100 };

        var result = TfmResolver.FindBestRefsTfm(Net48, refs);

        await Assert.That(result).IsEqualTo(Net48);
    }

    /// <summary>Empty refs/ list returns null without parsing anything.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FindBestRefsTfmReturnsNullWhenNoRefs()
    {
        var result = TfmResolver.FindBestRefsTfm(Net100, []);

        await Assert.That(result).IsNull();
    }

    /// <summary>Modern platform-suffixed TFMs report their platform label.</summary>
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

    /// <summary>Legacy Xamarin / mono / UAP TFMs report their platform label.</summary>
    /// <param name="tfm">TFM under test.</param>
    /// <param name="expected">Expected platform label.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments(Monoandroid120, "android")]
    [Arguments(Xamarinios10, "ios")]
    [Arguments("xamarinmac20", "maccatalyst")]
    [Arguments("uap10.0", "windows")]
    public async Task GetPlatformLabelHandlesLegacyMonikers(string tfm, string expected)
    {
        var label = TfmResolver.GetPlatformLabel(tfm);

        await Assert.That(label).IsNotNull();
        await Assert.That(label).IsEqualTo(expected);
    }

    /// <summary>Plain TFMs without a platform suffix return null.</summary>
    /// <param name="tfm">TFM under test.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments(Net100)]
    [Arguments(Net80)]
    [Arguments(Net48)]
    [Arguments(Netstandard20)]
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
        var available = new List<string> { Net80, Net90, Net100 };

        var result = TfmResolver.SelectTfm(available, Net80, [Net100]);

        await Assert.That(result).IsEqualTo(Net80);
    }

    /// <summary>SelectTfm: a prefix override (<c>net8</c>) matches <c>net8.0</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectTfmHonoursPrefixOverride()
    {
        var available = new List<string> { Net80, Net90 };

        var result = TfmResolver.SelectTfm(available, "net8", [Net90]);

        await Assert.That(result).IsEqualTo(Net80);
    }

    /// <summary>SelectTfm: walks the preference list in order, returning the first available exact match.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectTfmWalksPreferenceListInOrder()
    {
        var available = new List<string> { Net80, Net100 };

        var result = TfmResolver.SelectTfm(available, tfmOverride: null, tfmPreference: [Net100, Net80]);

        await Assert.That(result).IsEqualTo(Net100);
    }

    /// <summary>SelectTfm: preference prefix (<c>net8</c>) matches <c>net8.0</c> when no exact match exists.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectTfmFallsBackToPreferencePrefix()
    {
        var available = new List<string> { Net80 };

        var result = TfmResolver.SelectTfm(available, tfmOverride: null, tfmPreference: ["net8"]);

        await Assert.That(result).IsEqualTo(Net80);
    }

    /// <summary>SelectTfm: with no exact / prefix / major-version match, falls back to the highest netstandard available.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectTfmFallsBackToHighestNetstandard()
    {
        var available = new List<string> { Netstandard20, Netstandard21 };

        var result = TfmResolver.SelectTfm(available, tfmOverride: null, tfmPreference: [Net100]);

        await Assert.That(result).IsEqualTo(Netstandard21);
    }

    /// <summary>SelectTfm: returns null when no preference match and no netstandard fallback is available.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectTfmReturnsNullWhenNothingMatches()
    {
        var available = new List<string> { Monoandroid120 };

        var result = TfmResolver.SelectTfm(available, tfmOverride: null, tfmPreference: [Net100]);

        await Assert.That(result).IsNull();
    }

    /// <summary>SelectTfm: an unmatched override falls through to the preference list (override is a priority hint, not a hard pin).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectTfmUnmatchedOverrideFallsThroughToPreference()
    {
        var available = new List<string> { Net80 };

        var result = TfmResolver.SelectTfm(available, Net60, [Net80]);

        await Assert.That(result).IsEqualTo(Net80);
    }

    /// <summary>SelectAllSupportedTfms: with no override, every TFM matching any preference (exact, prefix, or major version) is returned.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectAllSupportedTfmsCollectsAllPreferenceMatches()
    {
        var available = new List<string> { Net80, Net90, Net100, Monoandroid120 };

        var result = TfmResolver.SelectAllSupportedTfms(available, tfmOverride: null, tfmPreference: [Net80, Net100]);

        await Assert.That(result.Count).IsEqualTo(SelectAllSupportedTfmsCollectsAllPreferenceMatchesExpectedValue);
        await Assert.That(result).Contains(Net80);
        await Assert.That(result).Contains(Net100);
    }

    /// <summary>SelectAllSupportedTfms: an override pins the result to whichever single TFM SelectTfm picks.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectAllSupportedTfmsHonoursOverride()
    {
        var available = new List<string> { Net80, Net90 };

        var result = TfmResolver.SelectAllSupportedTfms(available, Net90, [Net80]);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0]).IsEqualTo(Net90);
    }

    /// <summary>SelectAllSupportedTfms: when no preference matches, falls back to every available netstandard variant (not just the highest).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectAllSupportedTfmsCollectsAllNetstandardOnFallback()
    {
        var available = new List<string> { Netstandard20, Netstandard21 };

        var result = TfmResolver.SelectAllSupportedTfms(available, tfmOverride: null, tfmPreference: [Net100]);

        await Assert.That(result.Count).IsEqualTo(SelectAllSupportedTfmsCollectsAllPreferenceMatchesExpectedValue);
        await Assert.That(result).Contains(Netstandard20);
        await Assert.That(result).Contains(Netstandard21);
    }

    /// <summary>SelectAllSupportedTfms: an unmatched override pins the result to whatever the SelectTfm fallback picks (here the preference match).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectAllSupportedTfmsUnmatchedOverridePinsToPreferenceFallback()
    {
        var available = new List<string> { Net80 };

        var result = TfmResolver.SelectAllSupportedTfms(available, Net60, [Net80]);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0]).IsEqualTo(Net80);
    }

    /// <summary>SelectCompatibleTfms: a modern .NET target picks up its own TFM plus every lower-version compatible bucket (incl. netstandard).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectCompatibleTfmsIncludesLowerVersionsAndNetstandard()
    {
        var available = new List<string> { Net80, Net60, Netstandard20, Netstandard21, Net48 };

        var result = TfmResolver.SelectCompatibleTfms(Net80, available);

        await Assert.That(result).Contains(Net80);
        await Assert.That(result).Contains(Net60);
        await Assert.That(result).Contains(Netstandard20);
        await Assert.That(result).Contains(Netstandard21);
        await Assert.That(result).DoesNotContain(Net48);
    }

    /// <summary>SelectCompatibleTfms: results are ordered by descending rank so the target's own bucket comes first.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectCompatibleTfmsOrdersHighestRankFirst()
    {
        var available = new List<string> { Netstandard20, Net60, Net80 };

        var result = TfmResolver.SelectCompatibleTfms(Net80, available);

        await Assert.That(result[0]).IsEqualTo(Net80);
        await Assert.That(result[1]).IsEqualTo(Net60);
        await Assert.That(result[SelectAllSupportedTfmsCollectsAllPreferenceMatchesExpectedValue]).IsEqualTo(Netstandard20);
    }

    /// <summary>SelectCompatibleTfms: a netstandard2.0 target excludes net8.0 (modern .NET libs aren't compatible with a netstandard consumer).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectCompatibleTfmsExcludesHigherTargetFrameworksWhenTargetIsNetstandard()
    {
        var available = new List<string> { Netstandard20, "netstandard1.6", Net80 };

        var result = TfmResolver.SelectCompatibleTfms(Netstandard20, available);

        await Assert.That(result).Contains(Netstandard20);
        await Assert.That(result).Contains("netstandard1.6");
        await Assert.That(result).DoesNotContain(Net80);
    }

    /// <summary>SelectCompatibleTfms: returns empty when nothing in availableTfms is reachable from the target.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectCompatibleTfmsReturnsEmptyWhenNothingMatches()
    {
        var available = new List<string> { Net48, "net472" };

        var result = TfmResolver.SelectCompatibleTfms(Netstandard20, available);

        await Assert.That(result.Count).IsEqualTo(0);
    }

    /// <summary>SelectCompatibleTfms: short-circuits cheaply on empty input.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectCompatibleTfmsReturnsEmptyForEmptyInput()
    {
        var result = TfmResolver.SelectCompatibleTfms(Net80, []);

        await Assert.That(result.Count).IsEqualTo(0);
    }

    /// <summary>SelectCompatibleTfms: validates input and rejects null/whitespace target TFM.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SelectCompatibleTfmsRejectsBlankTarget() => await Assert.That(static () => TfmResolver.SelectCompatibleTfms(string.Empty, [Net80])).Throws<ArgumentException>();

    /// <summary>
    /// FindBestRefsTfm: a non-netstandard lib TFM that the reducer cannot pair
    /// with any refs entry returns null (covers the non-netstandard branch of
    /// the reducer's null-result fallback in <c>FindBestRefsTfmSlow</c>).
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FindBestRefsTfmReturnsNullForLegacyLibAgainstModernRefs()
    {
        var refs = new List<string> { Net80, Net100 };

        var result = TfmResolver.FindBestRefsTfm(Monoandroid120, refs);

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
        IReadOnlyList<string> legacy = [MonoAndroid10, "MonoTouch10", Xamarinios10, "xamarinmac20", "xamarintvos10", "xamarinwatchos10", "net461"];

        var result = TfmResolver.HasOnlyLegacyTfms(legacy);

        await Assert.That(result).IsTrue();
    }

    /// <summary>HasOnlyLegacyTfms: silverlight / windows-phone / portable-* / win8 / uap legacy TFMs all classify as legacy.</summary>
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
        IReadOnlyList<string> mixed = [MonoAndroid10, Xamarinios10, Netstandard20];

        var result = TfmResolver.HasOnlyLegacyTfms(mixed);

        await Assert.That(result).IsFalse();
    }

    /// <summary>HasOnlyLegacyTfms: any modern .NET (net5+) variant is enough to mark the package as non-legacy.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task HasOnlyLegacyTfmsReturnsFalseWhenAnyModernNetIsPresent()
    {
        IReadOnlyList<string> mixed = [MonoAndroid10, Xamarinios10, Net80];

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
        await Assert.That(static () => TfmResolver.HasOnlyLegacyTfms(null!)).Throws<ArgumentNullException>();

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
    [Arguments(Net48)]
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
        await Assert.That(static () => TfmResolver.IsLegacyDotNetFramework(null!)).Throws<ArgumentNullException>();
}
