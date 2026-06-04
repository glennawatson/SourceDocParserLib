// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.SourceLink;

namespace SourceDocParser.IntegrationTests;

/// <summary>
/// End-to-end check on <see cref="SourceLinkValidator"/> using real
/// public URLs from the ReactiveUI repository on github.com -- picks
/// a real source file pinned to an immutable release commit as the
/// live target, plus a deliberately broken path on the same host so
/// both code paths (success and HTTP 404) run end-to-end through the
/// rate-limited pipeline.
/// </summary>
/// <remarks>
/// Network-dependent. Belongs in IntegrationTests rather than the
/// unit suite for the same reason <c>EndToEndPipelineTests</c> does:
/// CI environments that block outbound HTTP will fail it, and that's
/// the correct signal -- the validator's job is to reach out.
/// </remarks>
public class SourceLinkValidatorTests
{
    /// <summary>
    /// Real ReactiveUI source pinned to an immutable release commit (the 23.2.28 tag) rather than a
    /// moving branch ref. Files get moved/renamed on <c>main</c> over time (e.g. the primitives
    /// reshuffle relocated ReactiveObject.cs into its own folder), which would silently 404 a
    /// <c>main</c>-pinned probe and fail this test for reasons unrelated to the validator. A commit
    /// SHA is content-addressed and never moves, so the success path stays deterministic.
    /// </summary>
    private const string KnownLiveUrl =
        "https://raw.githubusercontent.com/reactiveui/ReactiveUI/7c1ab24d9c84ed225c738bdb9f1a2f2586c6a5bc/src/ReactiveUI/ReactiveObject/ReactiveObject.cs";

    /// <summary>Same host/commit, intentionally non-existent path so we exercise the 404 branch.</summary>
    private const string KnownMissingUrl =
        "https://raw.githubusercontent.com/reactiveui/ReactiveUI/7c1ab24d9c84ed225c738bdb9f1a2f2586c6a5bc/this-path-does-not-exist-xyz123.cs";

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
