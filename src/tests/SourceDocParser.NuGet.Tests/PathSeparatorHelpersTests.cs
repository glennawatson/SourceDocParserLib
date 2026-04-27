// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.NuGet.Infrastructure;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins slash normalization helpers used by archive and filesystem paths.
/// </summary>
public class PathSeparatorHelpersTests
{
    /// <summary>Mixed archive separators collapse to forward slashes and keep a trailing slash.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EnsureTrailingForwardSlashNormalisesMixedSeparators() =>
        await Assert.That(PathSeparatorHelpers.EnsureTrailingForwardSlash(@"ref\net8.0\"))
            .IsEqualTo("ref/net8.0/");

    /// <summary>Platform path normalization rewrites both slash forms to the current OS separator.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ToPlatformPathNormalisesMixedSeparators()
    {
        var expected = string.Join(Path.DirectorySeparatorChar, ["foo", "bar", "baz"]);
        await Assert.That(PathSeparatorHelpers.ToPlatformPath(@"foo/bar\baz")).IsEqualTo(expected);
    }
}
