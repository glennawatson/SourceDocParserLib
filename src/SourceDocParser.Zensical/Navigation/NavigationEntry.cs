// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Zensical.Navigation;

/// <summary>
/// One leaf in the nav graph -- a single type page.
/// </summary>
/// <param name="Title">Formatted display name (with generic placeholders).</param>
/// <param name="Path">Relative page path with forward slashes.</param>
public readonly record struct NavigationEntry(string Title, string Path);
