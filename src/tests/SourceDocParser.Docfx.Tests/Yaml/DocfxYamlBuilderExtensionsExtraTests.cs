// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;
using SourceDocParser.Docfx.Yaml;
using SourceDocParser.Model;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.Docfx.Tests.Yaml;

/// <summary>
/// Pins the secondary branches of <see cref="DocfxYamlBuilderExtensions"/>
/// -- the bare-empty-scalar form, seealso emission, qualified-scalar
/// fast paths, and the legacy single-arg <c>AppendTypeItem</c> shim.
/// </summary>
public class DocfxYamlBuilderExtensionsExtraTests
{
    /// <summary>An empty value writes the YAML empty-string sentinel <c>''</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendScalarEmitsEmptyStringSentinelForEmpty()
    {
        var sb = new StringBuilder().AppendScalar(string.Empty);

        await Assert.That(sb.ToString().Lf()).IsEqualTo("''");
    }

    /// <summary>An empty seealso array writes nothing.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendSeealsoNoOpsForEmpty()
    {
        var sb = new StringBuilder().AppendSeealso([]);

        await Assert.That(sb.Length).IsEqualTo(0);
    }

    /// <summary>A populated seealso array emits one CRef block per entry, stripping the prefix in altText.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendSeealsoEmitsCRefBlocks()
    {
        var sb = new StringBuilder();

        sb.AppendSeealso(["T:Foo.Bar", "M:Foo.Baz"]);

        var output = sb.ToString().Lf();
        await Assert.That(output).Contains("seealso:");
        await Assert.That(output).Contains("    commentId: T:Foo.Bar");
        await Assert.That(output).Contains("    altText: Foo.Bar");
        await Assert.That(output).Contains("    commentId: M:Foo.Baz");
        await Assert.That(output).Contains("    altText: Foo.Baz");
    }

    /// <summary>An empty left-hand side falls through to a plain right-hand scalar.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendQualifiedScalarEmptyLeftEmitsRightOnly()
    {
        var sb = new StringBuilder().AppendQualifiedScalar(string.Empty, '.', "Bar");

        await Assert.That(sb.ToString().Lf()).IsEqualTo("Bar");
    }

    /// <summary>An empty right-hand side falls through to a plain left-hand scalar.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendQualifiedScalarEmptyRightEmitsLeftOnly()
    {
        var sb = new StringBuilder().AppendQualifiedScalar("Foo", '.', string.Empty);

        await Assert.That(sb.ToString().Lf()).IsEqualTo("Foo");
    }

    /// <summary>A safe pair joins with the separator without quoting.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendQualifiedScalarJoinsSafePairBare()
    {
        var sb = new StringBuilder().AppendQualifiedScalar("Foo", '.', "Bar");

        await Assert.That(sb.ToString().Lf()).IsEqualTo("Foo.Bar");
    }

    /// <summary>An empty-string body skips emission entirely.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendBlockScalarSkipsEmptyValue()
    {
        var sb = new StringBuilder().AppendBlockScalar("  summary: ", string.Empty);

        await Assert.That(sb.Length).IsEqualTo(0);
    }

    /// <summary>A multi-line value triggers the literal block (<c>|-</c>) path.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendBlockScalarUsesLiteralBlockForMultiLine()
    {
        var sb = new StringBuilder().AppendBlockScalar("  summary: ", "first\nsecond");

        var output = sb.ToString().Lf();
        await Assert.That(output).Contains("summary: |-");
        await Assert.That(output).Contains("first");
        await Assert.That(output).Contains("second");
    }

    /// <summary>A null or empty value short-circuits without writing the prefix.</summary>
    /// <param name="value">Value under test (null or empty).</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task AppendIfPresentSkipsNullOrEmpty(string? value)
    {
        var sb = new StringBuilder().AppendIfPresent("  parent: ", value);

        await Assert.That(sb.Length).IsEqualTo(0);
    }

    /// <summary>A populated value emits the prefix + scalar + newline.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendIfPresentWritesPrefixedScalar()
    {
        var sb = new StringBuilder().AppendIfPresent("  parent: ", "Foo");

        await Assert.That(sb.ToString().Lf()).IsEqualTo("  parent: Foo\n");
    }

    /// <summary>Control characters below space fall through to the hex-escape branch.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendQuotedCharEscapesLowControlCharAsHex()
    {
        const char belChar = '\u0007';
        var sb = new StringBuilder().AppendQuotedChar(belChar);

        await Assert.That(sb.ToString().Lf()).IsEqualTo("\\x07");
    }

    /// <summary>The standard YAML escape pairs map to their backslash sequences.</summary>
    /// <param name="input">Character under test.</param>
    /// <param name="expected">Expected escape sequence.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments('"', "\\\"")]
    [Arguments('\\', "\\\\")]
    [Arguments('\n', "\\n")]
    [Arguments('\r', "\\r")]
    [Arguments('\t', "\\t")]
    public async Task AppendQuotedCharEmitsStandardEscapes(char input, string expected)
    {
        var sb = new StringBuilder().AppendQuotedChar(input);

        await Assert.That(sb.ToString().Lf()).IsEqualTo(expected);
    }

    /// <summary>Printable characters pass through verbatim.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendQuotedCharPassesThroughPrintable()
    {
        var sb = new StringBuilder().AppendQuotedChar('A');

        await Assert.That(sb.ToString().Lf()).IsEqualTo("A");
    }

    /// <summary>The static-constructor metadata name <c>.cctor</c> rewrites to <c>#cctor</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MemberIdForRewritesStaticConstructor()
    {
        var member = ConstructorMember(".cctor");

        var actual = DocfxYamlBuilderExtensions.MemberIdFor(member);

        await Assert.That(actual).IsEqualTo("#cctor");
    }

    /// <summary>The instance-constructor metadata name <c>.ctor</c> rewrites to <c>#ctor</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MemberIdForRewritesInstanceConstructor()
    {
        var member = ConstructorMember(".ctor");

        var actual = DocfxYamlBuilderExtensions.MemberIdFor(member);

        await Assert.That(actual).IsEqualTo("#ctor");
    }

    /// <summary>Non-constructor members pass their metadata name through unchanged.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MemberIdForPassesThroughOrdinaryName()
    {
        var member = ConstructorMember("DoThing");

        var actual = DocfxYamlBuilderExtensions.MemberIdFor(member);

        await Assert.That(actual).IsEqualTo("DoThing");
    }

    /// <summary>Strings without an XML-doc comment-id prefix are returned unchanged.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task StripCommentIdPrefixIfPresentReturnsInputWithoutPrefix()
    {
        var actual = DocfxYamlBuilderExtensions.StripCommentIdPrefixIfPresent("Foo.Bar");

        await Assert.That(actual).IsEqualTo("Foo.Bar");
    }

    /// <summary>A comment-id-prefixed string has the two-character prefix stripped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task StripCommentIdPrefixIfPresentTrimsPrefix()
    {
        var actual = DocfxYamlBuilderExtensions.StripCommentIdPrefixIfPresent("T:Foo.Bar");

        await Assert.That(actual).IsEqualTo("Foo.Bar");
    }

    /// <summary>The legacy single-arg <c>AppendTypeItem</c> overload routes through the empty index.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppendTypeItemLegacyOverloadDelegatesToEmptyIndex()
    {
        var sb = new StringBuilder();
        var type = TestData.ObjectType("Foo");

        sb.AppendTypeItem(type);

        var output = sb.ToString().Lf();
        await Assert.That(output).Contains("- uid: Foo");
        await Assert.That(output).Contains("commentId: Foo");
        await Assert.That(output).Contains("type: Class");
    }

    /// <summary>Builds a minimal <see cref="ApiMember"/> whose only relevant field for these tests is <c>Name</c>.</summary>
    /// <param name="name">Metadata-style name to assign.</param>
    /// <returns>A constructor-shaped member with default everything else.</returns>
    private static ApiMember ConstructorMember(string name) => new(
        Name: name,
        Uid: $"M:Test.{name}",
        Kind: ApiMemberKind.Constructor,
        IsStatic: false,
        IsExtension: false,
        IsRequired: false,
        IsVirtual: false,
        IsOverride: false,
        IsAbstract: false,
        IsSealed: false,
        Signature: string.Empty,
        Parameters: [],
        TypeParameters: [],
        ReturnType: null,
        ContainingTypeUid: "T:Test",
        ContainingTypeName: "Test",
        SourceUrl: null,
        Documentation: ApiDocumentation.Empty,
        IsObsolete: false,
        ObsoleteMessage: null,
        Attributes: []);
}
