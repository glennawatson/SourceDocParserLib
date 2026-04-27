// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;
using System.Xml;
using SourceDocParser.NuGet.Readers;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins <see cref="PackageSourceCredentialParser"/>: the per-element
/// classification predicates (open container vs inner add element,
/// matching close-tag detection), the source-name unescape (<c>_x0020_</c>
/// space encoding), and <c>%VAR%</c> environment-variable expansion.
/// Tested in isolation so a regression in any one rule fails on its
/// own line.
/// </summary>
public class PackageSourceCredentialParserTests
{
    /// <summary>Outside a source container, any non-add element opens a new container.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsCredentialChildOpensContainerOnNonAddElement()
    {
        var reader = ReaderOnFirstElement("<root><MySource><add /></MySource></root>", "MySource");

        await Assert.That(PackageSourceCredentialParser.IsCredentialChildElement(reader, currentSourceKey: null)).IsTrue();
    }

    /// <summary>Outside a source container, an <c>add</c> element is not credential content.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsCredentialChildIgnoresAddOutsideContainer()
    {
        var reader = ReaderOnFirstElement("<root><add key=\"x\" value=\"y\" /></root>", "add");

        await Assert.That(PackageSourceCredentialParser.IsCredentialChildElement(reader, currentSourceKey: null)).IsFalse();
    }

    /// <summary>Inside a source container, <c>add</c> elements are credential content.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsCredentialChildKeepsAddInsideContainer()
    {
        var reader = ReaderOnFirstElement("<root><add key=\"Username\" value=\"u\" /></root>", "add");

        await Assert.That(PackageSourceCredentialParser.IsCredentialChildElement(reader, currentSourceKey: "MySource")).IsTrue();
    }

    /// <summary>Source-name unescape decodes <c>_x0020_</c> to a regular space.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task UnescapeSourceNameDecodesSpaceEscape()
    {
        await Assert.That(PackageSourceCredentialParser.UnescapeSourceName("My_x0020_Source")).IsEqualTo("My Source");
        await Assert.That(PackageSourceCredentialParser.UnescapeSourceName("Plain")).IsEqualTo("Plain");
    }

    /// <summary>IsSourceContainerEnd matches the unescaped form against the open container's key.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsSourceContainerEndMatchesUnescaped()
    {
        var reader = EndReaderOn("<root><My_x0020_Source><add /></My_x0020_Source></root>", "My_x0020_Source");

        await Assert.That(PackageSourceCredentialParser.IsSourceContainerEnd(reader, "My Source")).IsTrue();
        await Assert.That(PackageSourceCredentialParser.IsSourceContainerEnd(reader, "Other")).IsFalse();
    }

    /// <summary>ExpandEnvironmentVariables resolves a defined variable.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExpandEnvironmentVariablesResolvesDefinedVariable()
    {
        var name = "SDP_TEST_VAR_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, "resolved");
        try
        {
            await Assert.That(PackageSourceCredentialParser.ExpandEnvironmentVariables($"%{name}%"))
                .IsEqualTo("resolved");
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    /// <summary>ExpandEnvironmentVariables leaves an unresolved variable literal so callers can detect missing config.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExpandEnvironmentVariablesLeavesUnresolvedLiteral()
    {
        var name = "SDP_DEFINITELY_NOT_SET_" + Guid.NewGuid().ToString("N");

        await Assert.That(PackageSourceCredentialParser.ExpandEnvironmentVariables($"prefix-%{name}%-suffix"))
            .IsEqualTo($"prefix-%{name}%-suffix");
    }

    /// <summary>ExpandEnvironmentVariables passes through values without env-var sequences unchanged.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExpandEnvironmentVariablesPassesThroughLiteralValues()
    {
        await Assert.That(PackageSourceCredentialParser.ExpandEnvironmentVariables("plain")).IsEqualTo("plain");
    }

    /// <summary>Parses <paramref name="xml"/> and advances to the first start element with <paramref name="elementName"/>.</summary>
    /// <param name="xml">XML document to parse.</param>
    /// <param name="elementName">Local name of the element to position on.</param>
    /// <returns>The XML reader positioned on the named start element.</returns>
    private static XmlReader ReaderOnFirstElement(string xml, string elementName)
    {
        var reader = XmlReader.Create(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == elementName)
            {
                return reader;
            }
        }

        throw new InvalidOperationException($"Element '{elementName}' not found in XML.");
    }

    /// <summary>Parses <paramref name="xml"/> and advances to the first end element with <paramref name="elementName"/>.</summary>
    /// <param name="xml">XML document to parse.</param>
    /// <param name="elementName">Local name of the end element to position on.</param>
    /// <returns>The XML reader positioned on the named end element.</returns>
    private static XmlReader EndReaderOn(string xml, string elementName)
    {
        var reader = XmlReader.Create(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == elementName)
            {
                return reader;
            }
        }

        throw new InvalidOperationException($"End element '{elementName}' not found in XML.");
    }
}
