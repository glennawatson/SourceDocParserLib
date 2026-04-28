// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.XmlDoc;

namespace SourceDocParser;

/// <summary>
/// Lazy facade over a raw <see cref="ApiDocumentation"/> that converts
/// each text-shaped field via the supplied <see cref="XmlDocToMarkdown"/>
/// on first access and caches the result. Emitters construct one
/// per symbol so only the fields a page actually reads pay any
/// conversion cost.
/// </summary>
internal sealed class RenderedDoc
{
    /// <summary>The walker-produced documentation carrying raw XML fragments.</summary>
    private readonly ApiDocumentation _raw;

    /// <summary>The XML->Markdown converter used to materialise each field on first access.</summary>
    private readonly XmlDocToMarkdown _converter;

    /// <summary>Cached <c>summary/</c> Markdown; null until first read.</summary>
    private string? _summary;

    /// <summary>Cached <c>remarks/</c> Markdown; null until first read.</summary>
    private string? _remarks;

    /// <summary>Cached <c>returns/</c> Markdown; null until first read.</summary>
    private string? _returns;

    /// <summary>Cached <c>value/</c> Markdown; null until first read.</summary>
    private string? _value;

    /// <summary>Cached rendered example fragments; null until first read.</summary>
    private string[]? _examples;

    /// <summary>Cached rendered parameter entries; null until first read.</summary>
    private DocEntry[]? _parameters;

    /// <summary>Cached rendered type-parameter entries; null until first read.</summary>
    private DocEntry[]? _typeParameters;

    /// <summary>Cached rendered exception entries; null until first read.</summary>
    private DocEntry[]? _exceptions;

    /// <summary>Initializes a new instance of the <see cref="RenderedDoc"/> class wrapping <paramref name="raw"/>.</summary>
    /// <param name="raw">The walker-produced documentation carrying raw inner-XML fragments.</param>
    /// <param name="converter">The emitter's converter -- wired with the matching <see cref="ICrefResolver"/>.</param>
    public RenderedDoc(ApiDocumentation raw, XmlDocToMarkdown converter)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(converter);
        _raw = raw;
        _converter = converter;
    }

    /// <summary>Gets the rendered <c>summary/</c> Markdown.</summary>
    public string Summary => _summary ??= _converter.Convert(_raw.Summary);

    /// <summary>Gets the rendered <c>remarks/</c> Markdown.</summary>
    public string Remarks => _remarks ??= _converter.Convert(_raw.Remarks);

    /// <summary>Gets the rendered <c>returns/</c> Markdown.</summary>
    public string Returns => _returns ??= _converter.Convert(_raw.Returns);

    /// <summary>Gets the rendered <c>value/</c> Markdown.</summary>
    public string Value => _value ??= _converter.Convert(_raw.Value);

    /// <summary>Gets the rendered example fragments, in declaration order.</summary>
    public string[] Examples => _examples ??= ConvertAll(_raw.Examples);

    /// <summary>Gets the rendered parameter description entries.</summary>
    public DocEntry[] Parameters => _parameters ??= ConvertEntries(_raw.Parameters);

    /// <summary>Gets the rendered type-parameter description entries.</summary>
    public DocEntry[] TypeParameters => _typeParameters ??= ConvertEntries(_raw.TypeParameters);

    /// <summary>Gets the rendered exception description entries.</summary>
    public DocEntry[] Exceptions => _exceptions ??= ConvertEntries(_raw.Exceptions);

    /// <summary>Gets the seealso UID strings -- passed through unchanged so the resolver can format them per emitter.</summary>
    public string[] SeeAlso => _raw.SeeAlso;

    /// <summary>Gets the inheritance marker -- display name only, never carries XML, so passes through.</summary>
    public string? InheritedFrom => _raw.InheritedFrom;

    /// <summary>
    /// Renders every fragment in <paramref name="fragments"/> via the
    /// cached converter; returns the input array when empty so
    /// undocumented symbols cost zero allocation on access.
    /// </summary>
    /// <param name="fragments">Raw inner-XML fragments to convert.</param>
    /// <returns>One Markdown string per input fragment.</returns>
    private string[] ConvertAll(string[] fragments)
    {
        if (fragments.Length is 0)
        {
            return fragments;
        }

        var result = new string[fragments.Length];
        for (var i = 0; i < fragments.Length; i++)
        {
            result[i] = _converter.Convert(fragments[i]);
        }

        return result;
    }

    /// <summary>Converts each entry's <see cref="DocEntry.Value"/>; key passes through unchanged.</summary>
    /// <param name="entries">Raw entries from the underlying documentation.</param>
    /// <returns>One DocEntry per input with the value rendered to Markdown.</returns>
    private DocEntry[] ConvertEntries(DocEntry[] entries)
    {
        if (entries.Length is 0)
        {
            return entries;
        }

        var result = new DocEntry[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            result[i] = entries[i] with { Value = _converter.Convert(entries[i].Value) };
        }

        return result;
    }
}
