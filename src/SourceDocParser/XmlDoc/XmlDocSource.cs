// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

/// <summary>
/// Parses a NuGet-shipped XML doc file once and indexes its member
/// entries by Roslyn member ID (e.g. T:Foo.Bar,
/// M:Foo.Bar.Baz(System.Int32)) so the matching paragraph of XML can
/// be returned in O(1) when Roslyn asks for it via the
/// FileXmlDocumentationProvider hook on a MetadataReference.
/// </summary>
/// <remarks>
/// Uses a hand-rolled forward scanner over the file's text rather than
/// XmlReader.ReadOuterXml per member. The XmlReader path allocated a
/// fresh StringWriter + XmlTextWriter + StringBuilder per element,
/// which dominated the doc-load allocation profile (~10% of the
/// LoadAndWalk budget). The scanner only allocates one string per
/// member (the captured element substring) plus the up-front
/// File.ReadAllText call.
/// </remarks>
public sealed class XmlDocSource : IXmlDocSource
{
    /// <summary>Initial capacity hint for the per-file member dictionary.</summary>
    private const int InitialMemberCapacity = 1024;

    /// <summary>Member ID → full member outer-XML lookup.</summary>
    private readonly Dictionary<string, string> _byMemberId;

    /// <summary>
    /// Initializes a new instance of the <see cref="XmlDocSource"/> class.
    /// </summary>
    /// <param name="byMemberId">Pre-built member-id → element-XML map.</param>
    internal XmlDocSource(Dictionary<string, string> byMemberId) => _byMemberId = byMemberId;

    /// <inheritdoc />
    public int Count => _byMemberId.Count;

    /// <summary>
    /// Loads <paramref name="xmlPath"/> and indexes every
    /// member element by its <c>name</c> attribute.
    /// </summary>
    /// <param name="xmlPath">Absolute path to the .xml file.</param>
    /// <returns>An indexed source over the file's contents.</returns>
    public static XmlDocSource Load(string xmlPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xmlPath);
        return new(BuildIndex(File.ReadAllText(xmlPath)));
    }

    /// <summary>
    /// Indexes <paramref name="content"/> directly without going
    /// through disk; used by tests and benchmarks that want to feed
    /// in synthetic doc text.
    /// </summary>
    /// <param name="content">Raw .xml file text.</param>
    /// <returns>An indexed source over <paramref name="content"/>.</returns>
    public static XmlDocSource FromString(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new(BuildIndex(content));
    }

    /// <inheritdoc />
    public string? Get(string memberId) => _byMemberId.GetValueOrDefault(memberId);

    /// <summary>
    /// Walks <paramref name="content"/> looking for
    /// member elements (with their name attribute and nested content).
    /// XML doc files always emit <c>name="..."</c> with double quotes
    /// and never nest member elements, so a forward
    /// substring scan is correct for every file the .NET tooling
    /// produces.
    /// </summary>
    /// <param name="content">Raw file text.</param>
    /// <returns>The populated member-id → element-XML map.</returns>
    private static Dictionary<string, string> BuildIndex(string content)
    {
        var byMemberId = new Dictionary<string, string>(InitialMemberCapacity, StringComparer.Ordinal);
        const string OpenTag = "<member";
        const string NameAttr = "name=\"";
        const string CloseTag = "</member>";
        var span = content.AsSpan();
        var pos = 0;

        while (pos < span.Length)
        {
            var openOffset = span[pos..].IndexOf(OpenTag, StringComparison.Ordinal);
            if (openOffset < 0)
            {
                break;
            }

            var elementStart = pos + openOffset;

            // Find the closing > of the start tag (could be self-closing /> or full >).
            var startTagEnd = span[elementStart..].IndexOf('>');
            if (startTagEnd < 0)
            {
                break;
            }

            var startTagSpan = span.Slice(elementStart, startTagEnd + 1);

            // Pull name="..." out of the start tag.
            var nameMarker = startTagSpan.IndexOf(NameAttr, StringComparison.Ordinal);
            if (nameMarker < 0)
            {
                pos = elementStart + startTagSpan.Length;
                continue;
            }

            var nameStart = nameMarker + NameAttr.Length;
            var nameEnd = startTagSpan[nameStart..].IndexOf('"');
            if (nameEnd < 0)
            {
                pos = elementStart + startTagSpan.Length;
                continue;
            }

            var memberId = startTagSpan.Slice(nameStart, nameEnd).ToString();

            int elementEnd;
            if (startTagSpan.Length >= 2 && startTagSpan[^2] == '/')
            {
                // Self-closing <member name="X"/>.
                elementEnd = elementStart + startTagSpan.Length;
            }
            else
            {
                var closeOffset = span[(elementStart + startTagSpan.Length)..].IndexOf(CloseTag, StringComparison.Ordinal);
                if (closeOffset < 0)
                {
                    break;
                }

                elementEnd = elementStart + startTagSpan.Length + closeOffset + CloseTag.Length;
            }

            byMemberId[memberId] = span[elementStart..elementEnd].ToString();
            pos = elementEnd;
        }

        return byMemberId;
    }
}
