// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Zensical.Pages;

/// <summary>
/// Shared text/path formatting helpers for the Zensical emitters.
/// </summary>
internal static class ZensicalEmitterHelpers
{
    /// <summary>
    /// Folder under which symbols in the global (unnamed) namespace are emitted.
    /// </summary>
    private const string GlobalNamespaceFolder = "_global/";

    /// <summary>
    /// Formats a type name with angle-bracket generic placeholders.
    /// </summary>
    /// <param name="name">Base type name.</param>
    /// <param name="arity">Generic arity.</param>
    /// <returns>The formatted display name.</returns>
    public static string FormatDisplayTypeName(string name, int arity)
    {
        if (arity is 0)
        {
            return name;
        }

        var suffix = new GenericPlaceholderFormatter(arity, '<', '>', ", ");
        return string.Create(
            name.Length + suffix.Length,
            (Name: name, Suffix: suffix),
            static (dest, state) =>
            {
                state.Name.CopyTo(dest);
                state.Suffix.WriteTo(dest[state.Name.Length..]);
            });
    }

    /// <summary>
    /// Formats a type name for use as a folder/file stem.
    /// </summary>
    /// <param name="name">Base type name.</param>
    /// <param name="arity">Generic arity.</param>
    /// <returns>The formatted path stem.</returns>
    public static string FormatPathTypeName(string name, int arity)
    {
        if (arity is 0)
        {
            return name;
        }

        var suffix = new GenericPlaceholderFormatter(arity, '{', '}', ",");
        return string.Create(
            name.Length + suffix.Length,
            (Name: name, Suffix: suffix),
            static (dest, state) =>
            {
                state.Name.CopyTo(dest);
                state.Suffix.WriteTo(dest[state.Name.Length..]);
            });
    }

    /// <summary>
    /// Formats a Mermaid-safe node name for a generic type.
    /// </summary>
    /// <param name="name">Base type name.</param>
    /// <param name="arity">Generic arity.</param>
    /// <returns>The Mermaid-safe node name.</returns>
    public static string FormatMermaidTypeName(string name, int arity)
    {
        if (arity is 0)
        {
            return name;
        }

        var suffix = new GenericPlaceholderFormatter(arity, '~', '~', ",");
        return string.Create(
            name.Length + suffix.Length,
            (Name: name, Suffix: suffix),
            static (dest, state) =>
            {
                state.Name.CopyTo(dest);
                state.Suffix.WriteTo(dest[state.Name.Length..]);
            });
    }

    /// <summary>
    /// Builds the relative path for a type page.
    /// </summary>
    /// <param name="namespaceName">Namespace of the type.</param>
    /// <param name="typeName">Simple type name.</param>
    /// <param name="arity">Generic arity.</param>
    /// <param name="extension">Output file extension.</param>
    /// <returns>The relative path.</returns>
    public static string BuildTypePath(string namespaceName, string typeName, int arity, string extension)
    {
        var namespacePrefix = new NamespacePathFormatter(namespaceName);
        var typeNameFormatter = new PathTypeNameFormatter(typeName, arity);
        return string.Create(
            namespacePrefix.Length + typeNameFormatter.Length + extension.Length,
            (NamespacePrefix: namespacePrefix, TypeName: typeNameFormatter, Extension: extension),
            static (dest, state) =>
            {
                var written = state.NamespacePrefix.WriteTo(dest);
                written += state.TypeName.WriteTo(dest[written..]);
                state.Extension.CopyTo(dest[written..]);
            });
    }

    /// <summary>
    /// Builds the relative path for a member page.
    /// </summary>
    /// <param name="namespaceName">Namespace of the declaring type.</param>
    /// <param name="typeName">Simple declaring type name.</param>
    /// <param name="arity">Generic arity of the declaring type.</param>
    /// <param name="memberName">Sanitised member file stem.</param>
    /// <param name="extension">Output file extension.</param>
    /// <returns>The relative path.</returns>
    public static string BuildMemberPath(
        string namespaceName,
        string typeName,
        int arity,
        string memberName,
        string extension)
    {
        var namespacePrefix = new NamespacePathFormatter(namespaceName);
        var typeFolder = new PathTypeNameFormatter(typeName, arity);
        return string.Create(
            namespacePrefix.Length + typeFolder.Length + memberName.Length + extension.Length + 1,
            (NamespacePrefix: namespacePrefix, TypeFolder: typeFolder, MemberName: memberName, Extension: extension),
            static (dest, state) =>
            {
                var written = state.NamespacePrefix.WriteTo(dest);
                written += state.TypeFolder.WriteTo(dest[written..]);
                dest[written++] = '/';
                state.MemberName.CopyTo(dest[written..]);
                written += state.MemberName.Length;
                state.Extension.CopyTo(dest[written..]);
            });
    }

    /// <summary>
    /// Sanitises a string for safe use in a file name.
    /// </summary>
    /// <param name="name">Source text.</param>
    /// <returns>The sanitised name.</returns>
    public static string SanitiseForFilename(string name)
    {
        var replacementIndex = IndexOfFilenameReplacement(name);
        if (replacementIndex < 0)
        {
            return name;
        }

        return string.Create(
            name.Length,
            (Name: name, ReplacementIndex: replacementIndex),
            static (dest, state) =>
            {
                state.Name.AsSpan(0, state.ReplacementIndex).CopyTo(dest);
                for (var i = state.ReplacementIndex; i < state.Name.Length; i++)
                {
                    dest[i] = state.Name[i] switch
                    {
                        '.' or ':' => '_',
                        '<' => '{',
                        '>' => '}',
                        var c => c,
                    };
                }
            });
    }

    /// <summary>
    /// Escapes pipes and normalises line endings for a Markdown table cell.
    /// </summary>
    /// <param name="text">The cell content.</param>
    /// <returns>The escaped table cell content.</returns>
    public static string EscapeTableCell(string text)
    {
        var firstEscapeIndex = text.AsSpan().IndexOfAny(['|', '\n', '\r']);
        if (firstEscapeIndex < 0)
        {
            return text;
        }

        return string.Create(
            text.Length + CountEscapedPipes(text.AsSpan(firstEscapeIndex)),
            (Text: text, FirstEscapeIndex: firstEscapeIndex),
            static (dest, state) =>
            {
                state.Text.AsSpan(0, state.FirstEscapeIndex).CopyTo(dest);
                var destIndex = state.FirstEscapeIndex;
                for (var i = state.FirstEscapeIndex; i < state.Text.Length; i++)
                {
                    switch (state.Text[i])
                    {
                        case '|':
                            {
                                dest[destIndex++] = '\\';
                                dest[destIndex++] = '|';
                                break;
                            }

                        case '\n':
                        case '\r':
                            {
                                dest[destIndex++] = ' ';
                                break;
                            }

                        default:
                            {
                                dest[destIndex++] = state.Text[i];
                                break;
                            }
                    }
                }
            });
    }

    /// <summary>
    /// Escapes pipe characters for inline Markdown code and text.
    /// </summary>
    /// <param name="text">Source text.</param>
    /// <returns>The escaped text.</returns>
    public static string EscapeInlinePipes(string text)
    {
        var firstPipeIndex = text.IndexOf('|');
        if (firstPipeIndex < 0)
        {
            return text;
        }

        return string.Create(
            text.Length + CountEscapedPipes(text.AsSpan(firstPipeIndex)),
            (Text: text, FirstPipeIndex: firstPipeIndex),
            static (dest, state) =>
            {
                state.Text.AsSpan(0, state.FirstPipeIndex).CopyTo(dest);
                var destIndex = state.FirstPipeIndex;
                for (var i = state.FirstPipeIndex; i < state.Text.Length; i++)
                {
                    if (state.Text[i] is '|')
                    {
                        dest[destIndex++] = '\\';
                    }

                    dest[destIndex++] = state.Text[i];
                }
            });
    }

    /// <summary>
    /// Trims a summary, keeps only the first paragraph, and flattens it to a single line.
    /// </summary>
    /// <param name="summary">Raw summary text.</param>
    /// <param name="escapePipes">Whether pipe characters should be escaped.</param>
    /// <returns>The flattened summary.</returns>
    public static string FirstParagraphAsSingleLine(string summary, bool escapePipes = false)
    {
        if (summary is not [_, ..])
        {
            return string.Empty;
        }

        var trimmed = summary.AsSpan().Trim();
        var paragraphBreak = trimmed.IndexOf("\n\n", StringComparison.Ordinal);
        var firstParagraph = paragraphBreak >= 0 ? trimmed[..paragraphBreak] : trimmed;
        return ToSingleLine(firstParagraph, escapePipes).Trim();
    }

    /// <summary>
    /// Flattens a span to a single line and optionally escapes pipes.
    /// </summary>
    /// <param name="text">Source text.</param>
    /// <param name="escapePipes">Whether pipe characters should be escaped.</param>
    /// <returns>The flattened string.</returns>
    private static string ToSingleLine(in ReadOnlySpan<char> text, bool escapePipes)
    {
        var firstEscapeIndex = escapePipes
            ? text.IndexOfAny(['|', '\n', '\r'])
            : text.IndexOfAny(['\n', '\r']);
        if (firstEscapeIndex < 0)
        {
            return text.ToString();
        }

        return string.Create(
            text.Length + (escapePipes ? CountEscapedPipes(text[firstEscapeIndex..]) : 0),
            (Text: text.ToString(), FirstEscapeIndex: firstEscapeIndex, EscapePipes: escapePipes),
            static (dest, state) =>
            {
                state.Text.AsSpan(0, state.FirstEscapeIndex).CopyTo(dest);
                var destIndex = state.FirstEscapeIndex;
                for (var i = state.FirstEscapeIndex; i < state.Text.Length; i++)
                {
                    var current = state.Text[i];
                    if (current is '\n' or '\r')
                    {
                        dest[destIndex++] = ' ';
                        continue;
                    }

                    if (state.EscapePipes && current is '|')
                    {
                        dest[destIndex++] = '\\';
                    }

                    dest[destIndex++] = current;
                }
            });
    }

    /// <summary>
    /// Writes a positive integer into the destination span.
    /// </summary>
    /// <param name="dest">Destination span.</param>
    /// <param name="value">Positive integer value.</param>
    /// <returns>Characters written.</returns>
    private static int WritePositiveInt(in Span<char> dest, int value)
    {
        var digits = CountDigits(value);
        for (var i = digits - 1; i >= 0; i--)
        {
            dest[i] = (char)('0' + (value % 10));
            value /= 10;
        }

        return digits;
    }

    /// <summary>
    /// Counts decimal digits in a positive integer.
    /// </summary>
    /// <param name="value">Value to count digits for.</param>
    /// <returns>Decimal digit count.</returns>
    private static int CountDigits(int value)
    {
        var digits = 1;
        while (value >= 10)
        {
            value /= 10;
            digits++;
        }

        return digits;
    }

    /// <summary>
    /// Counts pipes in the span so the escaped table-cell length can be computed up front.
    /// </summary>
    /// <param name="text">Text to scan.</param>
    /// <returns>The number of extra backslashes needed.</returns>
    private static int CountEscapedPipes(in ReadOnlySpan<char> text)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            count += text[i] is '|' ? 1 : 0;
        }

        return count;
    }

    /// <summary>
    /// Finds the first filename character that needs replacement.
    /// </summary>
    /// <param name="text">Source text.</param>
    /// <returns>The first replacement index, or -1.</returns>
    private static int IndexOfFilenameReplacement(string text) => text.AsSpan().IndexOfAny(['.', '<', '>', ':']);

    /// <summary>
    /// Represents a generic placeholder suffix as one unit so the length and write paths evolve together.
    /// </summary>
    /// <param name="Arity">Generic arity.</param>
    /// <param name="Open">Opening delimiter.</param>
    /// <param name="Close">Closing delimiter.</param>
    /// <param name="Separator">Separator between placeholders.</param>
    private readonly record struct GenericPlaceholderFormatter(int Arity, char Open, char Close, string Separator)
    {
        /// <summary>Gets the total rendered length including the delimiters.</summary>
        public int Length => GetPlaceholderContentLength() + 2;

        /// <summary>
        /// Writes the formatted suffix into <paramref name="dest"/>.
        /// </summary>
        /// <param name="dest">Destination span.</param>
        /// <returns>Characters written.</returns>
        public int WriteTo(in Span<char> dest)
        {
            var index = 0;
            dest[index++] = Open;
            for (var i = 0; i < Arity; i++)
            {
                if (i > 0)
                {
                    Separator.CopyTo(dest[index..]);
                    index += Separator.Length;
                }
                else if (Arity is 1)
                {
                    dest[index++] = 'T';
                    continue;
                }

                dest[index++] = 'T';
                index += WritePositiveInt(dest[index..], i + 1);
            }

            dest[index++] = Close;
            return index;
        }

        /// <summary>
        /// Returns the rendered length excluding the surrounding delimiters.
        /// </summary>
        /// <returns>Placeholder content length.</returns>
        private int GetPlaceholderContentLength()
        {
            if (Arity == 1)
            {
                return 1;
            }

            var length = 0;
            for (var i = 1; i <= Arity; i++)
            {
                length += 1 + CountDigits(i);
            }

            return length + ((Arity - 1) * Separator.Length);
        }
    }

    /// <summary>
    /// Represents a namespace path prefix as one unit so the rendered length and write path stay aligned.
    /// </summary>
    /// <param name="NamespaceName">Namespace to render.</param>
    private readonly record struct NamespacePathFormatter(string NamespaceName)
    {
        /// <summary>Gets the rendered length including the trailing slash.</summary>
        public int Length => NamespaceName is [] ? GlobalNamespaceFolder.Length : NamespaceName.Length + 1;

        /// <summary>
        /// Writes the namespace prefix into <paramref name="dest"/>.
        /// </summary>
        /// <param name="dest">Destination span.</param>
        /// <returns>Characters written.</returns>
        public int WriteTo(in Span<char> dest)
        {
            if (NamespaceName is [])
            {
                GlobalNamespaceFolder.AsSpan().CopyTo(dest);
                return GlobalNamespaceFolder.Length;
            }

            for (var i = 0; i < NamespaceName.Length; i++)
            {
                dest[i] = NamespaceName[i] == '.' ? '/' : NamespaceName[i];
            }

            dest[NamespaceName.Length] = '/';
            return NamespaceName.Length + 1;
        }
    }

    /// <summary>
    /// Represents a type name rendered for a path segment, including any generic placeholders.
    /// </summary>
    /// <param name="Name">Base type name.</param>
    /// <param name="Arity">Generic arity.</param>
    private readonly record struct PathTypeNameFormatter(string Name, int Arity)
    {
        /// <summary>Gets the rendered path length.</summary>
        public int Length => Arity is 0
            ? Name.Length
            : Name.Length + new GenericPlaceholderFormatter(Arity, '{', '}', ",").Length;

        /// <summary>
        /// Writes the formatted path name into <paramref name="dest"/>.
        /// </summary>
        /// <param name="dest">Destination span.</param>
        /// <returns>Characters written.</returns>
        public int WriteTo(in Span<char> dest)
        {
            Name.AsSpan().CopyTo(dest);
            if (Arity == 0)
            {
                return Name.Length;
            }

            var suffix = new GenericPlaceholderFormatter(Arity, '{', '}', ",");
            return Name.Length + suffix.WriteTo(dest[Name.Length..]);
        }
    }
}
