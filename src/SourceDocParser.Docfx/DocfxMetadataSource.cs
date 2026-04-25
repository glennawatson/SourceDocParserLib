// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Docfx;

/// <summary>
/// One entry in a docfx metadata <c>src</c> array. Identifies the source
/// directory and the explicit list of assemblies inside it that docfx should
/// process for this metadata block.
/// </summary>
/// <param name="Src">Repository-relative path to the source directory (for example <c>api/lib/net10.0</c>).</param>
/// <param name="Files">Explicit list of file names within <see cref="Src"/> that docfx should consume. Filenames only; the path comes from <see cref="Src"/>.</param>
public sealed record DocfxMetadataSource(string Src, List<string> Files);
