// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SamplePdb;

/// <summary>
/// Anchor type with a known method body line so the SourceLink /
/// PDB integration tests can pin <c>GetMethodLocation</c>'s row
/// arithmetic against a real assembly. Tests get the type's
/// <see cref="System.Reflection.Assembly.Location"/> and walk it
/// through SourceLinkReader to confirm the embedded portable PDB +
/// SourceLink JSON round-trip end to end.
/// </summary>
public class SamplePdbAnchor
{
    /// <summary>The body line below — kept on its own well-known line so the test fixture can hard-code it.</summary>
    public const int KnownMethodBodyLine = 22;

    /// <summary>Anchor body that always lives on <see cref="KnownMethodBodyLine"/>.</summary>
    /// <returns>A constant; the value isn't important.</returns>
    public static int Anchor() => 42;
}
