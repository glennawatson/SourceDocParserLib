// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace SourceDocParser;

/// <summary>
/// Roslyn DocumentationProvider that resolves doc text from an
/// XmlDocSource. Hooked onto a MetadataReference at creation time
/// (see <see cref="MetadataReferenceCache"/>) so that
/// <c>ISymbol.GetDocumentationCommentXml()</c> returns the package's
/// shipped doc text natively, identity-keyed via Roslyn's symbol ID
/// format. Avoids taking a dependency on
/// <c>Microsoft.CodeAnalysis.Workspaces</c> (which would drag in MEF
/// and a pile of editor types) just to get an XML doc loader.
/// </summary>
internal sealed class FileXmlDocumentationProvider(XmlDocSource source, string identity) : DocumentationProvider
{
    /// <summary>
    /// Indexed XML doc data.
    /// </summary>
    private readonly XmlDocSource _source = source;

    /// <summary>
    /// Identity used for equality (typically the .xml file path).
    /// </summary>
    private readonly string _identity = identity;

    /// <summary>
    /// Checks if two providers share the same identity.
    /// </summary>
    /// <param name="obj">Other provider to compare.</param>
    /// <returns>True if identities match.</returns>
    public override bool Equals(object? obj) =>
        obj is FileXmlDocumentationProvider other
            && string.Equals(_identity, other._identity, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        StringComparer.Ordinal.GetHashCode(_identity);

    /// <summary>
    /// Roslyn lookup hook.
    /// </summary>
    /// <param name="documentationMemberID">Roslyn member ID being resolved.</param>
    /// <param name="preferredCulture">Ignored as shipped XML docs are not localized.</param>
    /// <param name="cancellationToken">Standard cancellation token.</param>
    /// <returns>Matching member XML or an empty string.</returns>
    protected override string GetDocumentationForSymbol(
        string documentationMemberID,
        CultureInfo preferredCulture,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _source.Get(documentationMemberID) ?? string.Empty;
    }
}
