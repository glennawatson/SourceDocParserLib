// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.XmlDoc;

/// <summary>
/// Tracks whether the writer is emitting prose, bullet-list
/// items, or numbered-list items. Used by nested writers to
/// decide whether a paragraph break should also reset list
/// numbering — though most of our doc XML is shallow enough
/// that this stays at None most of the time.
/// </summary>
public enum ListContext
{
    /// <summary>Top-level prose, no surrounding list.</summary>
    None,

    /// <summary>Inside a bullet list item.</summary>
    Bullet,

    /// <summary>Inside a numbered list item.</summary>
    Numbered,
}
