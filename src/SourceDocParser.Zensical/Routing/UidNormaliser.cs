// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Zensical.Routing;

/// <summary>
/// Maps walker-emitted commentId-style UIDs to the canonical
/// open-generic form used by docfx and Microsoft Learn URLs.
/// The walker emits <c>T:System.Action{`0}</c> when a type is
/// referenced via a constructed instance (substituting the
/// containing type's own parameters back into itself); the
/// canonical form is <c>T:System.Action`1</c> with the arity
/// backtick. Without normalisation an autoref key never resolves
/// and a Microsoft Learn URL built from the raw form 404s.
/// </summary>
internal static class UidNormaliser
{
    /// <summary>
    /// Converts <paramref name="uid"/> to its autoref-safe form for
    /// use in mkdocs-autorefs anchors (<c>[](){#id}</c>) and
    /// reference links (<c>[text][id]</c>). Applies
    /// <see cref="Normalise"/> then translates the arity backtick to
    /// a hyphen -- backticks inside a Markdown reference label are
    /// treated as inline-code delimiters by Python-Markdown and break
    /// the lookup, and they're awkward inside attribute-list anchor
    /// IDs too. Both sides of the autoref pair (anchor + reference)
    /// MUST go through this method so they agree.
    /// </summary>
    /// <param name="uid">A commentId-style UID (may contain a generic-arity backtick).</param>
    /// <returns>The autoref-safe id string.</returns>
    public static string ToAutorefId(string uid) =>
        Normalise(uid).Replace('`', '-');

    /// <summary>
    /// Returns the canonical open-generic form of <paramref name="uid"/>.
    /// Strips any <c>{...}</c> generic-instantiation suffix and
    /// replaces it with <c>`N</c> where N is the type-parameter
    /// arity. Non-generic UIDs and already-canonical UIDs pass
    /// through unchanged.
    /// </summary>
    /// <param name="uid">A commentId-style UID (e.g. <c>T:System.Action{`0}</c>, <c>M:Foo.Bar(System.Int32)</c>, <c>T:System.String</c>).</param>
    /// <returns>The canonical UID.</returns>
    public static string Normalise(string uid)
    {
        ArgumentNullException.ThrowIfNull(uid);
        if (uid is [])
        {
            return uid;
        }

        // The encoding: a top-level <c>{...}</c> immediately follows
        // the type name. We're only normalising TYPE UIDs here --
        // member UIDs (M:/P:/E:/F:) carry argument lists in
        // parentheses and need their nested generic args left alone.
        if (uid is not [_, ':', ..])
        {
            return TryNormaliseBareTypeName(uid);
        }

        // Only T: gets the open-generic rewrite; M:/P:/E:/F:/N:
        // either don't carry the suffix or carry it as part of a
        // method signature where rewriting would lose information.
        if (uid is not ['T', ':', ..])
        {
            return uid;
        }

        var body = uid[2..];
        var normalisedBody = TryNormaliseBareTypeName(body);
        return ReferenceEquals(normalisedBody, body) ? uid : "T:" + normalisedBody;
    }

    /// <summary>
    /// Strips a top-level <c>{...}</c> generic-instantiation suffix
    /// from a bare type name and replaces it with the matching
    /// arity backtick.
    /// </summary>
    /// <param name="typeName">Bare type name (no commentId prefix).</param>
    /// <returns>The canonical bare name.</returns>
    private static string TryNormaliseBareTypeName(string typeName)
    {
        // Find the OUTERMOST opening brace. Anything after it (up
        // to the matching close) is the generic instantiation list.
        var openIndex = typeName.IndexOf('{');
        if (openIndex < 0)
        {
            return typeName;
        }

        var closeIndex = FindMatchingClose(typeName, openIndex);
        if (closeIndex < 0)
        {
            // Malformed (open without matching close); leave it alone.
            return typeName;
        }

        // Anything trailing the close (e.g. an array suffix) means
        // this isn't a simple constructed type -- leave it alone too.
        if (closeIndex != typeName.Length - 1)
        {
            return typeName;
        }

        var arity = CountTopLevelTypeArguments(typeName.AsSpan(openIndex + 1, closeIndex - openIndex - 1));
        if (arity <= 0)
        {
            return typeName;
        }

        return $"{typeName.AsSpan(0, openIndex)}`{arity.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    /// <summary>Finds the index of the brace that matches the open at <paramref name="openIndex"/>.</summary>
    /// <param name="text">Source text.</param>
    /// <param name="openIndex">Index of the opening <c>{</c>.</param>
    /// <returns>Index of the matching <c>}</c>, or -1 when none.</returns>
    private static int FindMatchingClose(string text, int openIndex)
    {
        var depth = 0;
        for (var i = openIndex; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '{':
                {
                    depth++;
                    break;
                }

                case '}':
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }

                    break;
                }
            }
        }

        return -1;
    }

    /// <summary>Counts top-level type arguments in <paramref name="argList"/> (comma-separated, ignoring nested braces).</summary>
    /// <param name="argList">Inside-the-braces span.</param>
    /// <returns>Argument count, or 0 when the span is empty.</returns>
    private static int CountTopLevelTypeArguments(in ReadOnlySpan<char> argList)
    {
        if (argList.IsEmpty)
        {
            return 0;
        }

        var count = 1;
        var depth = 0;
        for (var i = 0; i < argList.Length; i++)
        {
            switch (argList[i])
            {
                case '{':
                {
                    depth++;
                    break;
                }

                case '}':
                {
                    depth--;
                    break;
                }

                case ',' when depth == 0:
                {
                    count++;
                    break;
                }
            }
        }

        return count;
    }
}
