// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.SourceLink;

namespace SourceDocParser.Tests.SourceLink;

/// <summary>
/// Pins <see cref="SourceLinkMap"/>: wildcard prefix substitution,
/// the exact-match (non-wildcard) entry, the no-match return, and
/// the resolution cache.
/// </summary>
public class SourceLinkMapTests
{
    /// <summary>A wildcard entry resolves any path under the prefix to the URL with the relative path appended.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryResolveWildcardSubstitutesPath()
    {
        var map = new SourceLinkMap([new(@"C:\src\", "https://example/raw/", IsWildcard: true)]);

        var url = map.TryResolve(@"C:\src\foo\bar.cs");

        await Assert.That(url).IsEqualTo("https://example/raw/foo/bar.cs");
    }

    /// <summary>An exact-match entry resolves only when the local path equals the prefix.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryResolveExactMatchReturnsUrlPrefix()
    {
        var map = new SourceLinkMap([new(@"C:\src\foo.cs", "https://example/raw/foo.cs", IsWildcard: false)]);

        await Assert.That(map.TryResolve(@"C:\src\foo.cs")).IsEqualTo("https://example/raw/foo.cs");
        await Assert.That(map.TryResolve(@"C:\src\bar.cs")).IsNull();
    }

    /// <summary>A wildcard entry whose prefix doesn't match the local path is skipped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryResolveSkipsNonMatchingWildcard()
    {
        var map = new SourceLinkMap([new(@"D:\other\", "https://example/", IsWildcard: true)]);

        await Assert.That(map.TryResolve(@"C:\src\foo.cs")).IsNull();
    }

    /// <summary>The first matching entry in declaration order wins.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryResolveFirstEntryWins()
    {
        var map = new SourceLinkMap(
        [
            new(@"C:\src\", "https://first/", IsWildcard: true),
            new(@"C:\src\", "https://second/", IsWildcard: true),
        ]);

        var url = map.TryResolve(@"C:\src\foo.cs");

        await Assert.That(url!).StartsWith("https://first/");
    }

    /// <summary>A repeat lookup returns the cached value without re-walking the entry list.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryResolveCachesResolvedValue()
    {
        var map = new SourceLinkMap([new(@"C:\src\", "https://example/", IsWildcard: true)]);

        var first = map.TryResolve(@"C:\src\foo.cs");
        var second = map.TryResolve(@"C:\src\foo.cs");

        await Assert.That(first).IsEqualTo(second);
    }
}
