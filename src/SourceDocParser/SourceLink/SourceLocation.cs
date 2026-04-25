// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.SourceLink;

/// <summary>
/// A symbol's source location: file path and start line.
/// </summary>
/// <param name="LocalPath">File path recorded in the PDB.</param>
/// <param name="StartLine">First executable source line for the symbol.</param>
internal readonly record struct SourceLocation(string LocalPath, int StartLine);
