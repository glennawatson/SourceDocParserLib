// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.SourceLink;

namespace SourceDocParser.IntegrationTests;

/// <summary>
/// End-to-end check on <see cref="SourceLinkValidator"/> using real
/// public URLs from the ReactiveUI repository on github.com -- picks
/// a known-stable file under <c>src/ReactiveUI/</c> on the
/// <c>main</c> branch as the live target, plus a deliberately broken
/// path on the same host so both code paths (success and HTTP 404)
/// run end-to-end through the rate-limited pipeline.
/// </summary>
/// <remarks>
/// Network-dependent. Belongs in IntegrationTests rather than the
/// unit suite for the same reason <c>EndToEndPipelineTests</c> does:
/// CI environments that block outbound HTTP will fail it, and that's
/// the correct signal -- the validator's job is to reach out.
/// </remarks>
public class SourceLinkValidatorTests
{
    /// <summary>Real ReactiveUI source on the main branch -- stable enough as a smoke probe.</summary>
    private const string KnownLiveUrl =
        "https://raw.githubusercontent.com/reactiveui/ReactiveUI/main/src/ReactiveUI/Observable.cs";

    /// <summary>Same host, intentionally non-existent path so we exercise the 404 branch.</summary>
    private const string KnownMissingUrl =
        "https://raw.githubusercontent.com/reactiveui/ReactiveUI/main/this-path-does-not-exist-xyz123.cs";

    /// <summary>
    /// A list containing only resolvable URLs returns zero broken
    /// entries -- confirms the success path of the rate-limited HEAD
    /// pipeline against a real github.com response.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ValidatesRealReactiveUiSourceLinkAsHealthy()
    {
        var validator = new SourceLinkValidator();
        SourceLinkEntry[] entries = [new("T:ReactiveUI.ReactiveObject", KnownLiveUrl)];

        var brokenCount = await validator.ValidateAsync(entries).ConfigureAwait(false);

        await Assert.That(brokenCount).IsEqualTo(0);
    }

    /// <summary>
    /// A 404 path on the same host returns one broken entry. Pins
    /// the failure-counting branch and confirms the resilience
    /// pipeline correctly differentiates HEAD success from
    /// status-code failure.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FlagsHttpFailureAsBroken()
    {
        var validator = new SourceLinkValidator();
        SourceLinkEntry[] entries = [new("T:ReactiveUI.Missing", KnownMissingUrl)];

        var brokenCount = await validator.ValidateAsync(entries).ConfigureAwait(false);

        await Assert.That(brokenCount).IsEqualTo(1);
    }

    /// <summary>
    /// Mixed input -- one healthy URL, one missing -- surfaces exactly
    /// one broken entry. Also exercises the per-URL grouping by
    /// passing two entries for the same healthy URL so the dedupe
    /// logic collapses the HEAD calls.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DedupesAndReportsOnlyBrokenAcrossMixedInput()
    {
        var validator = new SourceLinkValidator();
        SourceLinkEntry[] entries =
        [
            new("T:ReactiveUI.ReactiveObject", KnownLiveUrl),
            new("M:ReactiveUI.ReactiveObject.RaisePropertyChanged", KnownLiveUrl),
            new("T:ReactiveUI.Missing", KnownMissingUrl),
        ];

        var brokenCount = await validator.ValidateAsync(entries).ConfigureAwait(false);

        await Assert.That(brokenCount).IsEqualTo(1);
    }

    /// <summary>
    /// Empty input short-circuits to zero. No HTTP traffic, no
    /// rate-limiter spin-up -- pins the early-return path.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmptyInputReturnsZeroWithoutTouchingNetwork()
    {
        var validator = new SourceLinkValidator();
        SourceLinkEntry[] entries = [];

        var brokenCount = await validator.ValidateAsync(entries).ConfigureAwait(false);

        await Assert.That(brokenCount).IsEqualTo(0);
    }

    /// <summary>
    /// failOnBroken: a broken URL with the flag set throws
    /// <see cref="InvalidOperationException"/> rather than returning
    /// a count -- pins the contract that build pipelines rely on for
    /// hard failure.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FailOnBrokenThrowsForBrokenLink()
    {
        var validator = new SourceLinkValidator();
        SourceLinkEntry[] entries = [new("T:ReactiveUI.Missing", KnownMissingUrl)];

        await Assert.That(() => validator.ValidateAsync(entries, failOnBroken: true))
            .Throws<InvalidOperationException>();
    }
}
