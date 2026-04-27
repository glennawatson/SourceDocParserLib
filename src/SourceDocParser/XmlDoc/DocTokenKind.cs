// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.XmlDoc;

/// <summary>Token kinds produced by <see cref="DocXmlScanner"/>.</summary>
internal enum DocTokenKind
{
    /// <summary>No token (initial state, or end-of-input).</summary>
    None,

    /// <summary>A start tag was just consumed; check <see cref="DocXmlScanner.IsEmptyElement"/> for self-closing.</summary>
    StartElement,

    /// <summary>An end tag was just consumed.</summary>
    EndElement,

    /// <summary>Character data (still entity-encoded).</summary>
    Text,
}
