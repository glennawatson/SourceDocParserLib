// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Common.Tests;

/// <summary>
/// Pins <see cref="CommentIdPrefix.Strip"/> + <see cref="CommentIdPrefix.StripSpan"/>
/// on every documented Roslyn comment-ID prefix. The two overloads must
/// agree on input/output character-for-character.
/// </summary>
public class CommentIdPrefixTests
{
    /// <summary>Each two-character Roslyn prefix gets stripped.</summary>
    /// <param name="prefixed">Prefixed comment ID.</param>
    /// <param name="bare">Expected bare-name result.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("T:Foo.Bar", "Foo.Bar")]
    [Arguments("M:Foo.Bar.Run(System.Int32)", "Foo.Bar.Run(System.Int32)")]
    [Arguments("P:Foo.Bar.Name", "Foo.Bar.Name")]
    [Arguments("F:Foo.Bar.Field", "Foo.Bar.Field")]
    [Arguments("E:Foo.Bar.Changed", "Foo.Bar.Changed")]
    [Arguments("N:Foo.Bar", "Foo.Bar")]
    public async Task PrefixesAreStripped(string prefixed, string bare)
    {
        await Assert.That(CommentIdPrefix.Strip(prefixed)).IsEqualTo(bare);
        await Assert.That(CommentIdPrefix.StripSpan(prefixed).ToString()).IsEqualTo(bare);
    }

    /// <summary>Inputs without a prefix flow through unchanged.</summary>
    /// <param name="input">Input under test.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("Foo.Bar")]
    [Arguments("PlainText")]
    [Arguments("")]
    public async Task UnprefixedInputsPassThrough(string input)
    {
        await Assert.That(CommentIdPrefix.Strip(input)).IsEqualTo(input);
        await Assert.That(CommentIdPrefix.StripSpan(input).ToString()).IsEqualTo(input);
    }

    /// <summary>A single character with no colon is too short to be a prefix.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SingleCharInputPassesThrough()
    {
        await Assert.That(CommentIdPrefix.Strip("X")).IsEqualTo("X");
        await Assert.That(CommentIdPrefix.StripSpan("X").ToString()).IsEqualTo("X");
    }
}
