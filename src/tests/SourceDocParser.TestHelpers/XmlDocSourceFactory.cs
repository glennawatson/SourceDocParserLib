// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.XmlDoc;

namespace SourceDocParser.TestHelpers;

/// <summary>
/// Test-only factory that builds an <see cref="XmlDocSource"/> from a
/// raw XML string. Lives in the helpers project (not in the
/// production assembly) so the production <c>SourceDocParser.dll</c>
/// stays free of test-only synthetic-input seams. Callers in test /
/// benchmark projects use this to stage in-memory doc fixtures
/// without touching the real <c>File.ReadAllBytes</c> load path.
/// </summary>
public static class XmlDocSourceFactory
{
    /// <summary>
    /// Indexes <paramref name="content"/> directly without going
    /// through disk -- mirrors the production <see cref="XmlDocSource.Load(string)"/>
    /// shape but takes the raw text as input.
    /// </summary>
    /// <param name="content">Raw .xml file text.</param>
    /// <returns>An indexed source over <paramref name="content"/>.</returns>
    public static XmlDocSource FromString(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new(content, XmlDocSource.BuildIndex(content));
    }
}
