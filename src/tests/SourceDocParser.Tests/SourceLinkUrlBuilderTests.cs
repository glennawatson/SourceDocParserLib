// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.SourceLink;

namespace SourceDocParser.Tests;

/// <summary>
/// Pins <see cref="SourceLinkUrlBuilder"/> wildcard URL construction so
/// SourceLink map resolution can stay focused on match ordering.
/// </summary>
public class SourceLinkUrlBuilderTests
{
    /// <summary>
    /// Mixed platform separators in the local suffix are normalised to forward slashes.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildWildcardUrlNormalisesDirectorySeparators()
    {
        const string prefix = "https://raw.example.com/repo/";
        const string localPath = "C:/src\\nested/file.cs";

        await Assert.That(SourceLinkUrlBuilder.BuildWildcardUrl(prefix, localPath, "C:/".Length))
            .IsEqualTo("https://raw.example.com/repo/src/nested/file.cs");
    }

    /// <summary>
    /// An empty wildcard suffix returns the original URL prefix unchanged.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildWildcardUrlReturnsPrefixForEmptySuffix()
    {
        const string prefix = "https://raw.example.com/repo/";
        const string localPath = "C:/src";

        await Assert.That(SourceLinkUrlBuilder.BuildWildcardUrl(prefix, localPath, localPath.Length))
            .IsEqualTo(prefix);
    }
}
