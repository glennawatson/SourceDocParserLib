// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;

namespace SourceDocParser.XmlDoc;

/// <summary>
/// Parses a NuGet-shipped XML doc file once and indexes its member
/// entries by Roslyn member ID (e.g. T:Foo.Bar,
/// M:Foo.Bar.Baz(System.Int32)) so the matching paragraph of XML can
/// be returned in O(1) when Roslyn asks for it via the
/// FileXmlDocumentationProvider hook on a MetadataReference.
/// </summary>
/// <remarks>
/// Uses a hand-rolled forward scanner over the file's text rather than
/// XmlReader.ReadOuterXml per member. Stores per-member offset/length
/// ranges instead of materialising the element substring at load time
/// — the substring is only allocated when a consumer asks for it via
/// Get(). Keeps peak memory bounded to one source string plus a small
/// Range entry per member, instead of thousands of small per-member
/// strings hanging off the dictionary.
///
/// Thread safety: build-once-then-read-many. Both factories build the
/// internal dictionary and return; nothing writes after that. Get() is
/// safe to call from multiple threads concurrently — the parallel
/// walker fans out symbol lookups across worker threads that share a
/// compilation, and Roslyn routes those into this source via the
/// FileXmlDocumentationProvider hook.
/// </remarks>
public sealed class XmlDocSource : IXmlDocSource
{
    /// <summary>Initial capacity hint for the per-file member dictionary.</summary>
    private const int InitialMemberCapacity = 1024;

    /// <summary>UTF-8 byte order mark prefix to strip from raw file content.</summary>
    private static readonly byte[] _utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>Raw .xml file content; member ranges index into this string.</summary>
    private readonly string _content;

    /// <summary>Member ID → (start offset, exclusive end) into <see cref="_content"/>.</summary>
    private readonly Dictionary<string, MemberRange> _ranges;

    /// <summary>
    /// Initializes a new instance of the <see cref="XmlDocSource"/> class.
    /// </summary>
    /// <param name="content">Raw .xml file text the ranges index into.</param>
    /// <param name="ranges">Member-id → range map.</param>
    internal XmlDocSource(string content, Dictionary<string, MemberRange> ranges)
    {
        _content = content;
        _ranges = ranges;
    }

    /// <inheritdoc />
    public int Count => _ranges.Count;

    /// <summary>
    /// Loads <paramref name="xmlPath"/> and indexes every
    /// member element by its <c>name</c> attribute.
    /// </summary>
    /// <param name="xmlPath">Absolute path to the .xml file.</param>
    /// <returns>An indexed source over the file's contents.</returns>
    public static XmlDocSource Load(string xmlPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xmlPath);

        // ReadAllBytes + GetString allocates one byte[] sized to the
        // file plus one string. The default ReadAllText path goes via
        // StreamReader which grows a StringBuilder in 1KB increments
        // and then ToStrings it — several extra MB of transient
        // allocation per assembly.
        var bytes = File.ReadAllBytes(xmlPath);
        var bytesSpan = bytes.AsSpan();
        if (bytesSpan.StartsWith(_utf8Bom))
        {
            bytesSpan = bytesSpan[_utf8Bom.Length..];
        }

        var content = Encoding.UTF8.GetString(bytesSpan);
        return new(content, BuildIndex(content));
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
        return new(content, BuildIndex(content));
    }

    /// <inheritdoc />
    public string? Get(string memberId) =>
        _ranges.TryGetValue(memberId, out var range) ? _content[range.Start..range.End] : null;

    /// <summary>
    /// Walks <paramref name="content"/> looking for
    /// member elements (with their name attribute and nested content).
    /// XML doc files always emit <c>name="..."</c> with double quotes
    /// and never nest member elements, so a forward
    /// substring scan is correct for every file the .NET tooling
    /// produces.
    /// </summary>
    /// <param name="content">Raw file text.</param>
    /// <returns>The populated member-id → range map.</returns>
    private static Dictionary<string, MemberRange> BuildIndex(string content)
    {
        var ranges = new Dictionary<string, MemberRange>(InitialMemberCapacity, StringComparer.Ordinal);
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

            var startTagEnd = span[elementStart..].IndexOf('>');
            if (startTagEnd < 0)
            {
                break;
            }

            var startTagSpan = span[elementStart..(elementStart + startTagEnd + 1)];

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

            var memberId = startTagSpan[nameStart..(nameStart + nameEnd)].ToString();

            int elementEnd;
            if (startTagSpan.Length >= 2 && startTagSpan[^2] == '/')
            {
                // Self-closing element form.
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

            ranges[memberId] = new(elementStart, elementEnd);
            pos = elementEnd;
        }

        return ranges;
    }

    /// <summary>Range into the source content for one member element.</summary>
    /// <param name="Start">Inclusive start index.</param>
    /// <param name="End">Exclusive end index.</param>
    internal readonly record struct MemberRange(int Start, int End);
}
