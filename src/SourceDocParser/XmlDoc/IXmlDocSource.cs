// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

/// <summary>
/// Indexed read-only view over a NuGet-shipped XML doc file. Backs
/// the <see cref="FileXmlDocumentationProvider"/> so Roslyn's
/// <c>ISymbol.GetDocumentationCommentXml()</c> hook can return the
/// shipped doc text without re-parsing the file.
/// </summary>
public interface IXmlDocSource
{
    /// <summary>Gets the number of indexed entries.</summary>
    int Count { get; }

    /// <summary>
    /// Returns the full member XML for <paramref name="memberId"/>, or
    /// null when the file does not contain an entry for it.
    /// </summary>
    /// <param name="memberId">Roslyn member ID, e.g. <c>T:Foo.Bar</c>, <c>M:Foo.Bar.Baz(System.Int32)</c>.</param>
    /// <returns>Matching XML string, or null when not found.</returns>
    string? Get(string memberId);
}
