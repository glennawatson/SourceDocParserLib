// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Docfx.Yaml;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.Docfx.Tests.Yaml;

/// <summary>
/// Pins the empty-Uid fallback paths of <see cref="DocfxCommentId"/> --
/// the wider emitter tests cover the populated-Uid path, but the
/// FullName fallback and the all-empty case only fire when the
/// walker can't synthesize an ID.
/// </summary>
public class DocfxCommentIdTests
{
    /// <summary>An empty Uid + populated FullName falls back to <c>T:FullName</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ForTypeFallsBackToFullNameWhenUidEmpty()
    {
        var type = TestData.ObjectType("Foo") with { Uid = string.Empty, FullName = "Some.Other.Foo" };

        var commentId = DocfxCommentId.ForType(type);

        await Assert.That(commentId).IsEqualTo("T:Some.Other.Foo");
    }

    /// <summary>Both Uid and FullName empty returns the empty string.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ForTypeReturnsEmptyWhenBothMissing()
    {
        var type = TestData.ObjectType("Foo") with { Uid = string.Empty, FullName = string.Empty };

        var commentId = DocfxCommentId.ForType(type);

        await Assert.That(commentId).IsEqualTo(string.Empty);
    }
}
