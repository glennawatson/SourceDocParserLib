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
}
