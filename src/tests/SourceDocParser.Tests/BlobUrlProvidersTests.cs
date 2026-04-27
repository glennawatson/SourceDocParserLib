// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.SourceLink;

namespace SourceDocParser.Tests;

/// <summary>
/// Pins each <see cref="BlobUrlProviders"/> per-host rewriter in
/// isolation: the dispatcher (covered by SourceUrlRewriterTests)
/// chains them in priority order, but per-provider tests confirm
/// each one returns <see langword="null"/> when its raw URL shape
/// doesn't match — that's the contract the dispatcher relies on.
/// </summary>
public class BlobUrlProvidersTests
{
    /// <summary>GitHub provider returns null for non-GitHub URLs.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GitHubReturnsNullForNonGitHubUrl()
    {
        await Assert.That(BlobUrlProviders.TryRewriteGitHub("https://example.org/foo.cs", 1)).IsNull();
        await Assert.That(BlobUrlProviders.TryRewriteGitHub("https://gitlab.com/x/y/-/raw/main/a.cs", 1)).IsNull();
    }

    /// <summary>GitHub raw URL with too few path segments returns null (no owner/repo/sha extractable).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GitHubReturnsNullForMalformedRawUrl() => await Assert.That(BlobUrlProviders.TryRewriteGitHub("https://raw.githubusercontent.com/owner/onlyone", 1)).IsNull();

    /// <summary>GitLab provider returns null for URLs without the <c>/-/raw/</c> segment.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GitLabReturnsNullWithoutRawSegment() => await Assert.That(BlobUrlProviders.TryRewriteGitLab("https://gitlab.com/x/y/blob/main/a.cs", 1)).IsNull();

    /// <summary>Bitbucket provider returns null for non-API URLs.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BitbucketReturnsNullForNonApiUrl() => await Assert.That(BlobUrlProviders.TryRewriteBitbucket("https://bitbucket.org/owner/repo/foo.cs", 1)).IsNull();

    /// <summary>Azure DevOps provider returns null when the URL doesn't match the dev.azure.com prefix.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AzureDevOpsReturnsNullForNonAzureUrl() => await Assert.That(BlobUrlProviders.TryRewriteAzureDevOps("https://example.org/foo.cs", 1)).IsNull();

    /// <summary>Azure DevOps provider returns null when the URL has the prefix but no <c>_apis/git/repositories/</c> segment.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AzureDevOpsReturnsNullWithoutGitApiSegment() => await Assert.That(BlobUrlProviders.TryRewriteAzureDevOps("https://dev.azure.com/org/project/foo.cs", 1)).IsNull();

    /// <summary>AppendDefaultAnchor returns the URL unchanged for line=0.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DefaultAnchorOmittedForZeroLine()
    {
        const string url = "https://example.org/foo.cs";
        await Assert.That(BlobUrlProviders.AppendDefaultAnchor(url, 0)).IsEqualTo(url);
    }

    /// <summary>AppendDefaultAnchor adds <c>#L{line}</c> for positive lines.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DefaultAnchorAppendedForPositiveLine() =>
        await Assert.That(BlobUrlProviders.AppendDefaultAnchor("https://example.org/foo.cs", 42))
            .IsEqualTo("https://example.org/foo.cs#L42");
}
