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
/// -- the substring is only allocated when a consumer asks for it via
/// Get(). Keeps peak memory bounded to one source string plus a small
/// Range entry per member, instead of thousands of small per-member
/// strings hanging off the dictionary.
///
/// Thread safety: build-once-then-read-many. Both factories build the
/// internal dictionary and return; nothing writes after that. Get() is
/// safe to call from multiple threads concurrently -- the parallel
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

    /// <summary>Member ID -> (start offset, exclusive end) into <see cref="_content"/>.</summary>
    private readonly Dictionary<string, MemberRange> _ranges;

    /// <summary>
    /// Initializes a new instance of the <see cref="XmlDocSource"/> class.
    /// </summary>
    /// <param name="content">Raw .xml file text the ranges index into.</param>
    /// <param name="ranges">Member-id -> range map.</param>
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
        // and then ToStrings it -- several extra MB of transient
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
    /// Attempts to load XML documentation sitting next to the assembly.
    /// </summary>
    /// <param name="assemblyPath">The absolute path to the assembly DLL.</param>
    /// <returns>A documentation source if the XML exists; otherwise, null.</returns>
    public static XmlDocSource? TryLoad(string assemblyPath)
    {
        var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
        return File.Exists(xmlPath) ? Load(xmlPath) : null;
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
    /// <returns>The populated member-id -> range map.</returns>
    internal static Dictionary<string, MemberRange> BuildIndex(string content)
    {
        var ranges = new Dictionary<string, MemberRange>(InitialMemberCapacity, StringComparer.Ordinal);
        var span = content.AsSpan();
        var pos = 0;

        while (pos < span.Length)
        {
            var step = TryIndexNextMemberElement(span, pos);
            if (step.Stop)
            {
                break;
            }

            if (step.MemberId is { } memberId)
            {
                ranges[memberId] = new(step.ElementStart, step.NextPosition);
            }

            pos = step.NextPosition;
        }

        return ranges;
    }

    /// <summary>
    /// Locates the next <c>member ...</c> element in
    /// <paramref name="span"/> at or after <paramref name="from"/>
    /// and returns the offsets needed to record (or skip) it. Pulled
    /// out of <see cref="BuildIndex"/> so the per-iteration parse
    /// reads at problem-domain level rather than as a chain of nested
    /// IndexOf checks.
    /// </summary>
    /// <param name="span">Raw file text.</param>
    /// <param name="from">Start position to scan from.</param>
    /// <returns>The step result.</returns>
    private static ScanStep TryIndexNextMemberElement(in ReadOnlySpan<char> span, int from)
    {
        const string OpenTag = "<member";
        const string NameAttr = "name=\"";
        const string CloseTag = "</member>";

        var openOffset = span[from..].IndexOf(OpenTag, StringComparison.Ordinal);
        if (openOffset < 0)
        {
            return ScanStep.End();
        }

        var elementStart = from + openOffset;
        var startTagEnd = span[elementStart..].IndexOf('>');
        if (startTagEnd < 0)
        {
            return ScanStep.End();
        }

        var startTagSpan = span[elementStart..(elementStart + startTagEnd + 1)];
        if (!TryReadNameAttribute(startTagSpan, NameAttr, out var memberId))
        {
            return ScanStep.SkipTo(elementStart + startTagSpan.Length);
        }

        // Self-closing form ends at the start tag's '>'; otherwise
        // walk to the matching </member>.
        if (startTagSpan is [.., '/', _])
        {
            return ScanStep.Record(memberId, elementStart, elementStart + startTagSpan.Length);
        }

        var closeOffset = span[(elementStart + startTagSpan.Length)..].IndexOf(CloseTag, StringComparison.Ordinal);
        if (closeOffset < 0)
        {
            return ScanStep.End();
        }

        return ScanStep.Record(memberId, elementStart, elementStart + startTagSpan.Length + closeOffset + CloseTag.Length);
    }

    /// <summary>
    /// Extracts the <c>name="..."</c> attribute value from a start
    /// tag, returning false when the attribute is missing or the
    /// closing quote can't be located.
    /// </summary>
    /// <param name="startTagSpan">The start-tag substring including the opening <c>&lt;</c> and closing <c>></c>.</param>
    /// <param name="nameAttr">The <c>name="</c> literal.</param>
    /// <param name="memberId">Receives the parsed member id.</param>
    /// <returns>True when the attribute was parsed.</returns>
    private static bool TryReadNameAttribute(in ReadOnlySpan<char> startTagSpan, string nameAttr, out string memberId)
    {
        memberId = string.Empty;
        var nameMarker = startTagSpan.IndexOf(nameAttr, StringComparison.Ordinal);
        if (nameMarker < 0)
        {
            return false;
        }

        var nameStart = nameMarker + nameAttr.Length;
        var nameEnd = startTagSpan[nameStart..].IndexOf('"');
        if (nameEnd < 0)
        {
            return false;
        }

        memberId = startTagSpan[nameStart..(nameStart + nameEnd)].ToString();
        return true;
    }

    /// <summary>Range into the source content for one member element.</summary>
    /// <param name="Start">Inclusive start index.</param>
    /// <param name="End">Exclusive end index.</param>
    internal readonly record struct MemberRange(int Start, int End);

    /// <summary>
    /// Outcome of one <see cref="TryIndexNextMemberElement"/> step:
    /// either record a member at the supplied range, skip past a
    /// malformed start tag, or stop scanning entirely.
    /// </summary>
    private readonly record struct ScanStep(bool Stop, string? MemberId, int ElementStart, int NextPosition)
    {
        /// <summary>Stop the outer scan loop.</summary>
        /// <returns>A stop step.</returns>
        public static ScanStep End() => new(Stop: true, MemberId: null, ElementStart: 0, NextPosition: 0);

        /// <summary>Skip past a malformed start tag and continue scanning.</summary>
        /// <param name="next">Next position to scan from.</param>
        /// <returns>A skip step.</returns>
        public static ScanStep SkipTo(int next) => new(Stop: false, MemberId: null, ElementStart: 0, NextPosition: next);

        /// <summary>Record a member element at <paramref name="elementStart"/>..<paramref name="next"/>.</summary>
        /// <param name="memberId">Parsed member id.</param>
        /// <param name="elementStart">Inclusive start of the recorded range.</param>
        /// <param name="next">Exclusive end (also the position to scan from next).</param>
        /// <returns>A record step.</returns>
        public static ScanStep Record(string memberId, int elementStart, int next) =>
            new(Stop: false, MemberId: memberId, ElementStart: elementStart, NextPosition: next);
    }
}
