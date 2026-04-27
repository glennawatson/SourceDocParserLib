// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Docfx.Yaml;

namespace SourceDocParser.Docfx.Tests.Yaml;

/// <summary>
/// Direct coverage of <see cref="YamlScalarQuoting"/>: the YAML
/// scalar-syntax predicates the docfx YAML emitter consults to decide
/// whether a value must be quoted to round-trip. The integration-level
/// emitter tests already validate rendered pages through YamlDotNet,
/// but the trigger set is wide enough to deserve focused per-rule
/// tests so a regression in any single case fails on its own line.
/// </summary>
public class YamlScalarQuotingTests
{
    /// <summary>Plain identifiers and dotted UIDs survive unquoted.</summary>
    /// <param name="value">Scalar value to probe.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("Foo")]
    [Arguments("System.Object")]
    [Arguments("M:Foo.Bar(System.Int32)")]
    [Arguments("snake_case_name")]
    [Arguments("123Numeric")]
    public async Task NeedsQuotingReturnsFalseForSafeIdentifiers(string value) => await Assert.That(YamlScalarQuoting.NeedsQuoting(value)).IsFalse();

    /// <summary>YAML reserved leading-indicator characters force quoting.</summary>
    /// <param name="value">Scalar value beginning with a reserved indicator.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("- leading dash")]
    [Arguments(": leading colon")]
    [Arguments("? leading question")]
    [Arguments("# leading hash")]
    [Arguments("& leading anchor")]
    [Arguments("* leading alias")]
    [Arguments("! leading tag")]
    [Arguments("| leading pipe")]
    [Arguments("> leading gt")]
    [Arguments("' leading quote")]
    [Arguments("\" leading dquote")]
    [Arguments("@ leading at")]
    [Arguments("` leading backtick")]
    [Arguments("% leading percent")]
    [Arguments("[ leading lbracket")]
    [Arguments("] leading rbracket")]
    [Arguments("{ leading lbrace")]
    [Arguments("} leading rbrace")]
    [Arguments(", leading comma")]
    [Arguments(" leading space")]
    [Arguments("\t leading tab")]
    public async Task NeedsQuotingReturnsTrueForReservedLeadingIndicators(string value) => await Assert.That(YamlScalarQuoting.NeedsQuoting(value)).IsTrue();

    /// <summary>YAML boolean / null reserved tokens force quoting in any case.</summary>
    /// <param name="value">Scalar value matching a reserved token.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("true")]
    [Arguments("false")]
    [Arguments("null")]
    [Arguments("True")]
    [Arguments("False")]
    [Arguments("Null")]
    [Arguments("TRUE")]
    [Arguments("FALSE")]
    [Arguments("NULL")]
    [Arguments("~")]
    [Arguments("yes")]
    [Arguments("no")]
    public async Task NeedsQuotingReturnsTrueForReservedTokens(string value) => await Assert.That(YamlScalarQuoting.NeedsQuoting(value)).IsTrue();

    /// <summary>
    /// Embedded characters that would terminate a plain scalar (control
    /// chars, quotes, backslash, newline, tab, and the context-sensitive
    /// <c>:</c>+space / space+<c>#</c> terminators) force the value
    /// into a quoted form.
    /// </summary>
    /// <param name="value">Scalar value containing a terminator.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("Foo: Bar")]
    [Arguments("Foo #Bar")]
    [Arguments("Foo\nBar")]
    [Arguments("Foo\"Bar")]
    [Arguments("Foo\\Bar")]
    [Arguments("Foo\tBar")]
    [Arguments("trailing:")]
    public async Task NeedsQuotingReturnsTrueForEmbeddedTerminators(string value) => await Assert.That(YamlScalarQuoting.NeedsQuoting(value)).IsTrue();

    /// <summary>
    /// Bare colons inside docfx UIDs (and bare hashes inside plain
    /// scalars) are valid YAML in plain form — only the
    /// space-terminated forms break the parser, so the predicate must
    /// stay quiet for embedded-but-not-terminating cases.
    /// </summary>
    /// <param name="value">Scalar with a benign embedded colon or hash.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("M:Foo.Bar(System.Int32)")]
    [Arguments("Foo:Bar")]
    [Arguments("Foo#Bar")]
    public async Task NeedsQuotingReturnsFalseForBenignEmbeddedColonOrHash(string value) => await Assert.That(YamlScalarQuoting.NeedsQuoting(value)).IsFalse();

    /// <summary>
    /// CompositeNeedsQuoting matches NeedsQuoting on the joined string
    /// for the common cases that round-trip through the qualified
    /// scalar writer. Verifies the single-pass composite stays in sync
    /// with the canonical predicate.
    /// </summary>
    /// <param name="left">Left half of the composite.</param>
    /// <param name="right">Right half of the composite.</param>
    /// <param name="expected">Expected predicate result.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("Foo", "Bar", false)]
    [Arguments("My.Lib", "Bar", false)]
    [Arguments("Foo", "M:Bar", false)]
    [Arguments(":left", "Bar", true)]
    [Arguments("Foo", ": right", true)]
    [Arguments("Foo", "right\nWithNewline", true)]
    [Arguments("trailing:", "Bar", false)]
    public async Task CompositeNeedsQuotingMatchesPerHalfPredicate(string left, string right, bool expected) => await Assert.That(YamlScalarQuoting.CompositeNeedsQuoting(left, '.', right)).IsEqualTo(expected);

    /// <summary>
    /// The colon-followed-by-separator case is benign when the
    /// separator isn't whitespace — a trailing <c>:</c> on the left
    /// half would be a problem if the separator were space, but for
    /// the dotted member-name composites we use it stays unquoted.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CompositeNeedsQuotingHandlesBoundaryColon()
    {
        await Assert.That(YamlScalarQuoting.CompositeNeedsQuoting("trailing:", '.', "Bar")).IsFalse();
        await Assert.That(YamlScalarQuoting.CompositeNeedsQuoting("trailing:", ' ', "Bar")).IsTrue();
    }

    /// <summary>
    /// The hash-preceded-by-separator case mirrors the colon rule —
    /// a leading <c>#</c> on the right half is dangerous only when the
    /// separator is whitespace.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CompositeNeedsQuotingHandlesBoundaryHash()
    {
        await Assert.That(YamlScalarQuoting.CompositeNeedsQuoting("Foo", '.', "#Bar")).IsFalse();
        await Assert.That(YamlScalarQuoting.CompositeNeedsQuoting("Foo", ' ', "#Bar")).IsTrue();
    }

    /// <summary>Reserved leading indicator detection — pinned per character so a regression in the trigger set fails on its own line.</summary>
    /// <param name="first">Leading character to test.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments(' ')]
    [Arguments('\t')]
    [Arguments('-')]
    [Arguments('?')]
    [Arguments(':')]
    [Arguments(',')]
    [Arguments('[')]
    [Arguments(']')]
    [Arguments('{')]
    [Arguments('}')]
    [Arguments('#')]
    [Arguments('&')]
    [Arguments('*')]
    [Arguments('!')]
    [Arguments('|')]
    [Arguments('>')]
    [Arguments('\'')]
    [Arguments('"')]
    [Arguments('%')]
    [Arguments('@')]
    [Arguments('`')]
    public async Task HasReservedLeadingIndicatorReturnsTrueForReservedChar(char first) => await Assert.That(YamlScalarQuoting.HasReservedLeadingIndicator(first)).IsTrue();

    /// <summary>Plain identifier characters aren't reserved leading indicators.</summary>
    /// <param name="first">Leading character to test.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments('A')]
    [Arguments('z')]
    [Arguments('0')]
    [Arguments('_')]
    [Arguments('.')]
    [Arguments('(')]
    public async Task HasReservedLeadingIndicatorReturnsFalseForSafeChar(char first) => await Assert.That(YamlScalarQuoting.HasReservedLeadingIndicator(first)).IsFalse();

    /// <summary>YAML 1.1 reserved boolean / null tokens.</summary>
    /// <param name="value">Token to test.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("true")]
    [Arguments("false")]
    [Arguments("null")]
    [Arguments("True")]
    [Arguments("False")]
    [Arguments("Null")]
    [Arguments("TRUE")]
    [Arguments("FALSE")]
    [Arguments("NULL")]
    [Arguments("~")]
    [Arguments("yes")]
    [Arguments("no")]
    public async Task IsReservedYamlTokenReturnsTrueForReservedToken(string value) => await Assert.That(YamlScalarQuoting.IsReservedYamlToken(value)).IsTrue();

    /// <summary>Mixed-case or near-miss values aren't reserved tokens.</summary>
    /// <param name="value">Value to test.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("tRue")]
    [Arguments("nULL")]
    [Arguments("YES")]
    [Arguments("Foo")]
    [Arguments("")]
    public async Task IsReservedYamlTokenReturnsFalseForOtherValues(string value) => await Assert.That(YamlScalarQuoting.IsReservedYamlToken(value)).IsFalse();

    /// <summary>ScanForTerminators flags every per-character condition that would terminate a plain scalar.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ScanForTerminatorsDetectsControlCharacters()
    {
        await Assert.That(YamlScalarQuoting.ScanForTerminators("Foo\nBar".AsSpan(), '\0', '\0')).IsTrue();
        await Assert.That(YamlScalarQuoting.ScanForTerminators("Foo\\Bar".AsSpan(), '\0', '\0')).IsTrue();
        await Assert.That(YamlScalarQuoting.ScanForTerminators("Foo\"Bar".AsSpan(), '\0', '\0')).IsTrue();
        await Assert.That(YamlScalarQuoting.ScanForTerminators("Foo\tBar".AsSpan(), '\0', '\0')).IsTrue();
    }

    /// <summary>ScanForTerminators consults <c>next</c> when a colon lands on the last character of the segment.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ScanForTerminatorsUsesNextForBoundaryColon()
    {
        await Assert.That(YamlScalarQuoting.ScanForTerminators("Foo:".AsSpan(), '\0', ' ')).IsTrue();
        await Assert.That(YamlScalarQuoting.ScanForTerminators("Foo:".AsSpan(), '\0', '.')).IsFalse();

        // Trailing colon with no boundary char ('\0') is treated as
        // end-of-value and triggers the key-terminator rule.
        await Assert.That(YamlScalarQuoting.ScanForTerminators("Foo:".AsSpan(), '\0', '\0')).IsTrue();
    }

    /// <summary>ScanForTerminators consults <c>prev</c> when a hash lands on the first character of the segment.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ScanForTerminatorsUsesPrevForBoundaryHash()
    {
        await Assert.That(YamlScalarQuoting.ScanForTerminators("#Foo".AsSpan(), ' ', '\0')).IsTrue();
        await Assert.That(YamlScalarQuoting.ScanForTerminators("#Foo".AsSpan(), '.', '\0')).IsFalse();

        // No preceding char ('\0') treats the hash as benign — the
        // composite caller is responsible for detecting a hash that
        // would actually start a comment.
        await Assert.That(YamlScalarQuoting.ScanForTerminators("#Foo".AsSpan(), '\0', '\0')).IsFalse();
    }
}
