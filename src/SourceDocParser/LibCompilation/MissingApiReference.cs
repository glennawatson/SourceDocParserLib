// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using Microsoft.CodeAnalysis;

namespace SourceDocParser.LibCompilation;

/// <summary>Identifies an unavailable assembly required to describe an API member.</summary>
/// <param name="AssemblyIdentity">The required assembly.</param>
/// <param name="Member">The API member requiring the assembly.</param>
[DebuggerDisplay("{AssemblyIdentity}: {Member,nq}")]
public readonly record struct MissingApiReference(AssemblyIdentity AssemblyIdentity, string Member);
