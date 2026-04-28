// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.TestHelpers;
using SourceDocParser.XmlDoc;

namespace SourceDocParser.Tests;

/// <summary>
/// Tests for <see cref="XmlDocSource"/> -- the indexed view over a
/// .NET XML doc file produced by the hand-rolled forward scanner.
/// </summary>
public class XmlDocSourceTests
{
    /// <summary>An empty doc parses to a source with zero entries.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FromStringHandlesEmptyDoc()
    {
        var source = XmlDocSourceFactory.FromString("<doc><members></members></doc>");

        await Assert.That(source.Count).IsEqualTo(0);
        await Assert.That(source.Get("T:Foo")).IsNull();
    }

    /// <summary>A simple member element indexes by its name attribute.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FromStringIndexesSingleMember()
    {
        const string xml = """
            <doc>
              <assembly><name>Foo</name></assembly>
              <members>
                <member name="T:Foo.Bar"><summary>The bar.</summary></member>
              </members>
            </doc>
            """;
        var source = XmlDocSourceFactory.FromString(xml);

        await Assert.That(source.Count).IsEqualTo(1);
        var entry = source.Get("T:Foo.Bar");
        await Assert.That(entry).IsNotNull();
        await Assert.That(entry!.Contains("The bar.", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>Multiple members each get their own entry, keyed by name.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FromStringIndexesMultipleMembers()
    {
        const string xml = """
            <doc><members>
              <member name="T:Foo.A"><summary>First.</summary></member>
              <member name="T:Foo.B"><summary>Second.</summary></member>
              <member name="M:Foo.A.Run"><summary>Run.</summary></member>
            </members></doc>
            """;
        var source = XmlDocSourceFactory.FromString(xml);

        await Assert.That(source.Count).IsEqualTo(3);
        await Assert.That(source.Get("T:Foo.A")).IsNotNull();
        await Assert.That(source.Get("T:Foo.B")).IsNotNull();
        await Assert.That(source.Get("M:Foo.A.Run")).IsNotNull();
    }

    /// <summary>Self-closing member entries are captured (they show up for empty doc bodies).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FromStringHandlesSelfClosingMember()
    {
        var source = XmlDocSourceFactory.FromString("""<doc><members><member name="T:Foo.Empty"/></members></doc>""");

        await Assert.That(source.Count).IsEqualTo(1);
        await Assert.That(source.Get("T:Foo.Empty")).IsNotNull();
    }

    /// <summary>Members without a name attribute are silently skipped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FromStringSkipsMembersWithoutName()
    {
        var source = XmlDocSourceFactory.FromString("<doc><members><member><summary>No name.</summary></member></members></doc>");

        await Assert.That(source.Count).IsEqualTo(0);
    }

    /// <summary>FromString validates its arguments.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FromStringValidatesArguments() =>
        await Assert.That(static () => XmlDocSourceFactory.FromString(null!)).Throws<ArgumentNullException>();

    /// <summary>Load validates its arguments.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task LoadValidatesArguments()
    {
        await Assert.That(static () => XmlDocSource.Load(null!)).Throws<ArgumentException>();
        await Assert.That(static () => XmlDocSource.Load("   ")).Throws<ArgumentException>();
    }

    /// <summary>Round-trip via a temp file matches FromString.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task LoadRoundTripsViaDisk()
    {
        const string xml = """<doc><members><member name="T:Foo.Disk"><summary>From disk.</summary></member></members></doc>""";
        var tempPath = Path.Combine(Path.GetTempPath(), $"sdp-xml-{Guid.NewGuid():N}.xml");
        await File.WriteAllTextAsync(tempPath, xml);
        try
        {
            var source = XmlDocSource.Load(tempPath);

            await Assert.That(source.Count).IsEqualTo(1);
            await Assert.That(source.Get("T:Foo.Disk")).IsNotNull();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
