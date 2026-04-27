// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.CSharp;
using SourceDocParser.XmlDoc;

namespace SourceDocParser.Tests.XmlDoc;

/// <summary>
/// Direct coverage of <see cref="DocXmlParser"/>: parses one
/// member-level XML doc fragment into a
/// <see cref="SourceDocParser.Model.RawDocumentation"/>. Walks each
/// top-level handler — common single-value tags, multi-value tags
/// (param, typeparam, exception, example), seealso, and inheritdoc —
/// plus the IsCommonTag classifier.
/// </summary>
public class DocXmlParserTests
{
    /// <summary>The four single-value documentation tags are recognised as common.</summary>
    /// <param name="tag">Tag under test.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("summary")]
    [Arguments("remarks")]
    [Arguments("returns")]
    [Arguments("value")]
    public async Task IsCommonTagReturnsTrueForCommonTags(string tag) =>
        await Assert.That(DocXmlParser.IsCommonTag(tag.AsSpan())).IsTrue();

    /// <summary>Other element names are not classified as common single-value tags.</summary>
    /// <param name="tag">Tag under test.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("param")]
    [Arguments("typeparam")]
    [Arguments("exception")]
    [Arguments("seealso")]
    [Arguments("inheritdoc")]
    [Arguments("example")]
    [Arguments("unknown")]
    public async Task IsCommonTagReturnsFalseForOtherTags(string tag) =>
        await Assert.That(DocXmlParser.IsCommonTag(tag.AsSpan())).IsFalse();

    /// <summary>Summary, remarks, returns, and value flow into the matching state field.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ParseFlowsCommonSingleValueTags()
    {
        var raw = Parse("""
            <member>
              <summary>top</summary>
              <remarks>extra</remarks>
              <returns>a value</returns>
              <value>the value</value>
            </member>
            """);

        await Assert.That(raw.Summary).IsEqualTo("top");
        await Assert.That(raw.Remarks).IsEqualTo("extra");
        await Assert.That(raw.Returns).IsEqualTo("a value");
        await Assert.That(raw.Value).IsEqualTo("the value");
    }

    /// <summary>Examples accumulate in declaration order.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ParseAccumulatesExamples()
    {
        var raw = Parse("""
            <member>
              <example>first</example>
              <example>second</example>
            </member>
            """);

        await Assert.That(raw.Examples.Length).IsEqualTo(2);
        await Assert.That(raw.Examples[0]).IsEqualTo("first");
        await Assert.That(raw.Examples[1]).IsEqualTo("second");
    }

    /// <summary>Each <c>param</c> is captured with its name attribute.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ParseCapturesNamedParams()
    {
        var raw = Parse("""
            <member>
              <param name="a">first</param>
              <param name="b">second</param>
            </member>
            """);

        await Assert.That(raw.Parameters.Length).IsEqualTo(2);
        await Assert.That(raw.Parameters[0].Name).IsEqualTo("a");
        await Assert.That(raw.Parameters[0].Value).IsEqualTo("first");
        await Assert.That(raw.Parameters[1].Name).IsEqualTo("b");
    }

    /// <summary>A <c>param</c> with no name attribute is dropped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ParseDropsNamelessParams()
    {
        var raw = Parse("""
            <member>
              <param>orphan</param>
              <param name="ok">kept</param>
            </member>
            """);

        await Assert.That(raw.Parameters.Length).IsEqualTo(1);
        await Assert.That(raw.Parameters[0].Name).IsEqualTo("ok");
    }

    /// <summary>Type parameters are captured separately from value parameters.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ParseCapturesTypeParameters()
    {
        var raw = Parse("""
            <member>
              <typeparam name="T">type slot</typeparam>
            </member>
            """);

        await Assert.That(raw.TypeParameters.Length).IsEqualTo(1);
        await Assert.That(raw.TypeParameters[0].Name).IsEqualTo("T");
        await Assert.That(raw.TypeParameters[0].Value).IsEqualTo("type slot");
    }

    /// <summary>Exceptions store the cref as the entry name.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ParseCapturesExceptionCref()
    {
        var raw = Parse("""
            <member>
              <exception cref="T:System.ArgumentNullException">on null</exception>
            </member>
            """);

        await Assert.That(raw.Exceptions.Length).IsEqualTo(1);
        await Assert.That(raw.Exceptions[0].Name).IsEqualTo("T:System.ArgumentNullException");
        await Assert.That(raw.Exceptions[0].Value).IsEqualTo("on null");
    }

    /// <summary>Top-level seealso entries land in the SeeAlso list.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ParseCapturesSeeAlso()
    {
        var raw = Parse("""
            <member>
              <seealso cref="T:Foo" />
              <seealso cref="T:Bar" />
            </member>
            """);

        await Assert.That(raw.SeeAlso.Length).IsEqualTo(2);
        await Assert.That(raw.SeeAlso[0]).IsEqualTo("T:Foo");
        await Assert.That(raw.SeeAlso[1]).IsEqualTo("T:Bar");
    }

    /// <summary>An <c>inheritdoc</c> with a cref sets HasInheritDoc and stores the cref.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ParseCapturesInheritDocWithCref()
    {
        var raw = Parse("""<member><inheritdoc cref="T:Foo" /></member>""");

        await Assert.That(raw.HasInheritDoc).IsTrue();
        await Assert.That(raw.InheritDocCref).IsEqualTo("T:Foo");
    }

    /// <summary>An <c>inheritdoc</c> without a cref still sets HasInheritDoc.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ParseSetsInheritDocFlagWithoutCref()
    {
        var raw = Parse("<member><inheritdoc /></member>");

        await Assert.That(raw.HasInheritDoc).IsTrue();
        await Assert.That(raw.InheritDocCref).IsNull();
    }

    /// <summary>Unknown top-level elements are silently dropped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ParseIgnoresUnknownElements()
    {
        var raw = Parse("""
            <member>
              <weird>nope</weird>
              <summary>kept</summary>
            </member>
            """);

        await Assert.That(raw.Summary).IsEqualTo("kept");
    }

    /// <summary>Constructs a <see cref="DocResolveContext"/> backed by an
    /// empty Roslyn compilation, then runs <see cref="DocXmlParser.Parse"/>.
    /// As of v0.3 the parser captures raw inner XML — the converter is
    /// no longer invoked at parse time.</summary>
    /// <param name="memberXml">Raw member XML to parse.</param>
    /// <returns>The parsed raw documentation.</returns>
    private static SourceDocParser.Model.RawDocumentation Parse(string memberXml)
    {
        var compilation = CSharpCompilation.Create("Probe");
        var context = new DocResolveContext(compilation, new DocResolveCache());
        return DocXmlParser.Parse(memberXml, context);
    }
}
