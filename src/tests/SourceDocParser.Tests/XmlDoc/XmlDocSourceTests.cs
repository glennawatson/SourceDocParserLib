// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.TestHelpers;
using SourceDocParser.XmlDoc;

namespace SourceDocParser.Tests.XmlDoc;

/// <summary>
/// Direct coverage of <see cref="XmlDocSource"/> + the underlying
/// <see cref="XmlDocSource.BuildIndex"/> scanner. Drives every
/// branch of the forward-only member walk through synthetic XML
/// fragments so a regression in any single edge case (self-closing,
/// missing name, missing close tag) lands with a focused failure.
/// </summary>
public class XmlDocSourceTests
{
    /// <summary>Well-formed doc XML maps each member-id to a substring carrying the full element.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildIndexCapturesEachMemberElement()
    {
        var source = XmlDocSourceFactory.FromString(
            """
            <?xml version="1.0"?>
            <doc>
              <members>
                <member name="T:Foo">
                  <summary>Plain type.</summary>
                </member>
                <member name="M:Foo.Bar">
                  <summary>Method.</summary>
                </member>
              </members>
            </doc>
            """);

        await Assert.That(source.Count).IsEqualTo(2);

        var typeXml = source.Get("T:Foo");
        await Assert.That(typeXml).IsNotNull();
        await Assert.That(typeXml!).Contains("<summary>Plain type.</summary>");

        var methodXml = source.Get("M:Foo.Bar");
        await Assert.That(methodXml).IsNotNull();
        await Assert.That(methodXml!).Contains("<summary>Method.</summary>");
    }

    /// <summary>Self-closing <c>member name="..." /</c> entries are captured at their tag length (no body).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildIndexHandlesSelfClosingMember()
    {
        var source = XmlDocSourceFactory.FromString(
            """
            <doc>
              <members>
                <member name="T:Empty" />
              </members>
            </doc>
            """);

        await Assert.That(source.Count).IsEqualTo(1);
        var captured = source.Get("T:Empty")!;
        await Assert.That(captured).Contains("name=\"T:Empty\"");
        await Assert.That(captured).EndsWith("/>");
    }

    /// <summary>A <c>member</c> element with no <c>name=</c> attribute is skipped without breaking the walk.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildIndexSkipsMemberWithoutNameAttribute()
    {
        var source = XmlDocSourceFactory.FromString(
            """
            <doc>
              <members>
                <member>
                  <summary>Anonymous.</summary>
                </member>
                <member name="T:Foo">
                  <summary>Named.</summary>
                </member>
              </members>
            </doc>
            """);

        await Assert.That(source.Count).IsEqualTo(1);
        await Assert.That(source.Get("T:Foo")).IsNotNull();
    }

    /// <summary>A <c>member</c> opener (with attributes) with no <c>/member</c> close tag stops the walk; nothing is captured for that entry.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildIndexStopsOnMissingCloseTag()
    {
        var source = XmlDocSourceFactory.FromString(
            """
            <doc>
              <members>
                <member name="T:Closed"><summary>ok</summary></member>
                <member name="T:Open">
                  <summary>orphan</summary>
            """);

        // The first complete element captures cleanly; the second is
        // incomplete so the walk gives up before adding it. This is
        // the documented behaviour -- a real .NET-emitted file is
        // never truncated mid-element, and the scanner stops cleanly.
        await Assert.That(source.Get("T:Closed")).IsNotNull();
        await Assert.That(source.Get("T:Open")).IsNull();
    }

    /// <summary>A name attribute with no closing quote is skipped -- the entry isn't captured but the walk continues from the start tag's end.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildIndexSkipsMemberWithUnterminatedNameAttribute()
    {
        var source = XmlDocSourceFactory.FromString(
            """
            <doc><members>
              <member name="bad>oops</member>
              <member name="T:Good"><summary>ok</summary></member>
            </members></doc>
            """);

        // "bad>oops" includes a `>` inside the name attribute value
        // before any closing quote -- the scanner takes the first '>'
        // as the start-tag terminator and the resulting `name="bad`
        // attribute body has no closing quote, so the entry is
        // skipped. The well-formed entry that follows is still found.
        await Assert.That(source.Get("T:Good")).IsNotNull();
    }

    /// <summary>A truncated <c>member</c> opener with no close-bracket anywhere terminates the walk before adding an entry.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildIndexStopsOnTruncatedMemberOpener()
    {
        var source = XmlDocSourceFactory.FromString(
            "<doc><members><member name=\"T:Closed\"/></members><member name=\"T:Trunc\"");

        // The first complete entry indexes; the truncated opener has
        // no '>' so the scanner gives up cleanly.
        await Assert.That(source.Get("T:Closed")).IsNotNull();
        await Assert.That(source.Get("T:Trunc")).IsNull();
    }

    /// <summary>Get returns null for a member id that wasn't indexed.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetReturnsNullForUnknownMemberId()
    {
        var source = XmlDocSourceFactory.FromString(
            "<doc><members><member name=\"T:Foo\" /></members></doc>");

        await Assert.That(source.Get("T:Missing")).IsNull();
    }

    /// <summary>Empty input produces an empty source; Count = 0 and every Get returns null.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildIndexReturnsEmptyForBlankInput()
    {
        var source = XmlDocSourceFactory.FromString(string.Empty);

        await Assert.That(source.Count).IsEqualTo(0);
        await Assert.That(source.Get("T:Anything")).IsNull();
    }

    /// <summary>Input without any <c>member</c> tag returns an empty source (the wrapping doc / members elements are skipped).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildIndexReturnsEmptyForInputWithoutMemberTags()
    {
        var source = XmlDocSourceFactory.FromString(
            "<doc><assembly><name>Foo</name></assembly></doc>");

        await Assert.That(source.Count).IsEqualTo(0);
    }

    /// <summary>FromString rejects null content with <see cref="ArgumentNullException"/>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FromStringRejectsNullContent() =>
        await Assert.That(static () => XmlDocSourceFactory.FromString(null!)).Throws<ArgumentNullException>();

    /// <summary>Load rejects blank or whitespace paths up front (no I/O).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task LoadRejectsBlankPath()
    {
        await Assert.That(static () => XmlDocSource.Load(string.Empty)).Throws<ArgumentException>();
        await Assert.That(static () => XmlDocSource.Load("   ")).Throws<ArgumentException>();
    }

    /// <summary>Load reads a real on-disk file end-to-end and indexes its member entries.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task LoadReadsFileFromDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            const string xml = """
                <doc><members>
                  <member name="T:DiskTest"><summary>From disk.</summary></member>
                </members></doc>
                """;
            await File.WriteAllTextAsync(path, xml);

            var source = XmlDocSource.Load(path);

            await Assert.That(source.Count).IsEqualTo(1);
            await Assert.That(source.Get("T:DiskTest")!).Contains("From disk.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Load strips a UTF-8 BOM from the file header before decoding.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task LoadStripsUtf8Bom()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            byte[] bom = [0xEF, 0xBB, 0xBF];
            const string xml = """<doc><members><member name="T:Bom"><summary>ok</summary></member></members></doc>""";
            byte[] body = System.Text.Encoding.UTF8.GetBytes(xml);
            byte[] full = new byte[bom.Length + body.Length];
            bom.CopyTo(full, 0);
            body.CopyTo(full, bom.Length);
            await File.WriteAllBytesAsync(path, full);

            var source = XmlDocSource.Load(path);

            await Assert.That(source.Get("T:Bom")).IsNotNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// TryLoad returns a source when the XML file exists next to the assembly.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryLoadReturnsSourceWhenXmlFileExists()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"sdp-tryload-{Guid.NewGuid():N}");
        var assemblyPath = Path.Combine(baseDir, "TestAssembly.dll");
        var xmlPath = Path.Combine(baseDir, "TestAssembly.xml");
        try
        {
            Directory.CreateDirectory(baseDir);
            await File.WriteAllTextAsync(xmlPath, "<doc><members><member name=\"T:A\">summary</member></members></doc>");

            var source = XmlDocSource.TryLoad(assemblyPath);
            await Assert.That(source).IsNotNull();
            await Assert.That(source!.Get("T:A")).IsEqualTo("<member name=\"T:A\">summary</member>");
        }
        finally
        {
            if (Directory.Exists(baseDir))
            {
                Directory.Delete(baseDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// TryLoad returns null when the XML file is missing.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryLoadReturnsNullWhenXmlFileIsMissing()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"sdp-tryload-missing-{Guid.NewGuid():N}");
        var assemblyPath = Path.Combine(baseDir, "TestAssembly.dll");
        try
        {
            Directory.CreateDirectory(baseDir);
            var source = XmlDocSource.TryLoad(assemblyPath);
            await Assert.That(source).IsNull();
        }
        finally
        {
            if (Directory.Exists(baseDir))
            {
                Directory.Delete(baseDir, recursive: true);
            }
        }
    }
}
