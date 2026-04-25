// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Xml;

namespace SourceDocParser;

/// <summary>
/// Parses a NuGet-shipped XML doc file once and indexes its member
/// entries by Roslyn member ID (e.g. <c>T:Foo.Bar</c>,
/// <c>M:Foo.Bar.Baz(System.Int32)</c>) so the matching paragraph of
/// XML can be returned in O(1) when Roslyn asks for it via the
/// DocumentationProvider hook on a MetadataReference.
///
/// Built around XmlReader rather than XDocument so a 1+ MB doc file
/// (ReactiveUI.xml is on that order) doesn't materialise the whole DOM
/// in memory before we extract what we need. Each member element gets
/// captured by its outer XML, so consumers see the full
/// <c>member name="..."</c> envelope just like
/// Roslyn's own <c>ISymbol.GetDocumentationCommentXml()</c> contract.
/// </summary>
internal sealed class XmlDocSource(Dictionary<string, string> byMemberId)
{
    /// <summary>
    /// Initial capacity hint for the per-file member dictionary.
    /// </summary>
    private const int InitialMemberCapacity = 1024;

    /// <summary>
    /// Member ID to full member outer XML lookup.
    /// </summary>
    private readonly Dictionary<string, string> _byMemberId = byMemberId;

    /// <summary>
    /// Gets the number of indexed entries.
    /// </summary>
    public int Count => _byMemberId.Count;

    /// <summary>
    /// Parses an XML file and returns an indexed source.
    /// </summary>
    /// <param name="xmlPath">Absolute path to the .xml file.</param>
    /// <returns>An indexed XmlDocSource.</returns>
    /// <remarks>
    /// Expected to follow the standard .NET XML doc shape with a top-level doc
    /// containing members, each having a member name attribute.
    /// </remarks>
    public static XmlDocSource Load(string xmlPath)
    {
        var byMemberId = new Dictionary<string, string>(capacity: InitialMemberCapacity, StringComparer.Ordinal);
        var settings = new XmlReaderSettings
        {
            IgnoreWhitespace = false,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            DtdProcessing = DtdProcessing.Ignore,
        };

        using var stream = File.OpenRead(xmlPath);
        using var reader = XmlReader.Create(stream, settings);
        while (reader.Read())
        {
            if (reader is not { NodeType: XmlNodeType.Element, Name: "member" })
            {
                continue;
            }

            if (reader.GetAttribute("name") is not { Length: > 0 } memberId)
            {
                continue;
            }

            // ReadOuterXml advances the reader past the element.
            byMemberId[memberId] = reader.ReadOuterXml();
        }

        return new(byMemberId);
    }

    /// <summary>
    /// Returns the full member XML for the supplied Roslyn member ID.
    /// </summary>
    /// <param name="memberId">Roslyn member ID, e.g. T:Foo.Bar.</param>
    /// <returns>Matching XML string or null if not found.</returns>
    public string? Get(string memberId) =>
        _byMemberId.TryGetValue(memberId, out var xml) ? xml : null;
}
