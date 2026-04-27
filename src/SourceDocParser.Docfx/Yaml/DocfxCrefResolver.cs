// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Docfx.Yaml;

/// <summary>
/// <see cref="ICrefResolver"/> implementation tuned for docfx's
/// ManagedReference YAML output. Renders cref references as docfx's
/// native <c>&lt;xref:UID&gt;</c> form so docfx's xrefmap step can
/// resolve them at site-build time, falling back to inline code for
/// the empty-UID and generic-parameter (<c>!:</c>) cases.
/// </summary>
public sealed class DocfxCrefResolver : ICrefResolver
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly DocfxCrefResolver Instance = new();

    /// <inheritdoc />
    public string Render(string uid, ReadOnlySpan<char> shortName)
    {
        if (uid is null or [])
        {
            return $"`{shortName}`";
        }

        if (uid is ['!', ':', ..])
        {
            return $"`{shortName}`";
        }

        // docfx's xrefmap resolves <xref:UID> at build time; the displayProperty
        // hint matches what docfx's own metadata extractor emits.
        return $"<xref:{uid}?displayProperty=nameWithType>";
    }
}
