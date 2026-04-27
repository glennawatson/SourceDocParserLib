// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.SourceLink;

namespace SourceDocParser.Tests;

/// <summary>
/// Pins <see cref="SourceUrlRewriter"/>: maps the raw SourceLink URLs
/// the four major Git hosts publish into human-friendly blob URLs with
/// a line anchor. Each host has its own rewrite shape — these tests
/// nail the conversion so a regression in any single host's path
/// surfaces on its own line.
/// </summary>
public class SourceUrlRewriterTests
{
    /// <summary>GitHub raw URLs become blob URLs with <c>#L{line}</c> anchor.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GitHubRawRewritesToBlobWithLineAnchor()
    {
        var rewritten = SourceUrlRewriter.ToBlobUrl(
            "https://raw.githubusercontent.com/owner/repo/abc123/src/Foo.cs",
            line: 42);

        await Assert.That(rewritten).IsEqualTo("https://github.com/owner/repo/blob/abc123/src/Foo.cs#L42");
    }

    /// <summary>Line=0 omits the anchor on GitHub URLs.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GitHubRawWithZeroLineOmitsAnchor()
    {
        var rewritten = SourceUrlRewriter.ToBlobUrl(
            "https://raw.githubusercontent.com/owner/repo/abc123/src/Foo.cs",
            line: 0);

        await Assert.That(rewritten).IsEqualTo("https://github.com/owner/repo/blob/abc123/src/Foo.cs");
    }

    /// <summary>GitLab <c>/-/raw/</c> segments swap to <c>/-/blob/</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GitLabRawRewritesToBlob()
    {
        var rewritten = SourceUrlRewriter.ToBlobUrl(
            "https://gitlab.com/owner/repo/-/raw/main/src/Foo.cs",
            line: 17);

        await Assert.That(rewritten).IsEqualTo("https://gitlab.com/owner/repo/-/blob/main/src/Foo.cs#L17");
    }

    /// <summary>Bitbucket API URLs rewrite to bitbucket.org with the special <c>#lines-{n}</c> anchor.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BitbucketApiRewritesToBlobWithLinesAnchor()
    {
        var rewritten = SourceUrlRewriter.ToBlobUrl(
            "https://api.bitbucket.org/2.0/repositories/owner/repo/src/abc123/src/Foo.cs",
            line: 99);

        await Assert.That(rewritten).IsEqualTo("https://bitbucket.org/owner/repo/src/abc123/src/Foo.cs#lines-99");
    }

    /// <summary>Azure DevOps API URLs rewrite to <c>/_git/</c> with path + GC{version} query.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AzureDevOpsApiRewritesToGitWithLineQuery()
    {
        var rewritten = SourceUrlRewriter.ToBlobUrl(
            "https://dev.azure.com/org/project/_apis/git/repositories/repo/items?path=/src/Foo.cs&version=abc123",
            line: 5);

        await Assert.That(rewritten).IsEqualTo(
            "https://dev.azure.com/org/project/_git/repo?path=/src/Foo.cs&version=GCabc123&line=5");
    }

    /// <summary>An Azure DevOps URL missing the <c>path</c> query parameter falls back to the raw URL.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AzureDevOpsWithoutPathFallsBackToRaw()
    {
        const string raw = "https://dev.azure.com/org/project/_apis/git/repositories/repo/items?version=abc123";

        var rewritten = SourceUrlRewriter.ToBlobUrl(raw, line: 5);

        await Assert.That(rewritten).IsEqualTo(raw + "#L5");
    }

    /// <summary>Unknown hosts pass through with a default <c>#L{line}</c> anchor.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task UnknownHostAppendsDefaultAnchor()
    {
        var rewritten = SourceUrlRewriter.ToBlobUrl("https://example.org/foo.cs", line: 12);

        await Assert.That(rewritten).IsEqualTo("https://example.org/foo.cs#L12");
    }

    /// <summary>Unknown hosts with line=0 return the raw URL unchanged.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task UnknownHostWithoutLineReturnsRaw()
    {
        const string raw = "https://example.org/foo.cs";

        var rewritten = SourceUrlRewriter.ToBlobUrl(raw, line: 0);

        await Assert.That(rewritten).IsEqualTo(raw);
    }

    /// <summary>StripAnchor drops everything from the first <c>#</c> onward.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task StripAnchorRemovesLineAnchor()
    {
        var stripped = SourceUrlRewriter.StripAnchor("https://github.com/owner/repo/blob/abc/src/Foo.cs#L42");

        await Assert.That(stripped).IsEqualTo("https://github.com/owner/repo/blob/abc/src/Foo.cs");
    }

    /// <summary>StripAnchor passes through a URL that has no anchor.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task StripAnchorPassesThroughWhenNoAnchor()
    {
        const string url = "https://github.com/owner/repo/blob/abc/src/Foo.cs";

        await Assert.That(SourceUrlRewriter.StripAnchor(url)).IsEqualTo(url);
    }
}
