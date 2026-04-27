// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SamplePdb;

/// <summary>
/// <c>[Flags]</c> enum with bitwise members + an explicit
/// <c>byte</c> underlying type — exercises the walker's enum
/// underlying-type capture and the <c>[Flags]</c> attribute thread
/// through.
/// </summary>
[Flags]
public enum SampleFlags : byte
{
    /// <summary>No bits set.</summary>
    None = 0,

    /// <summary>Bit 0.</summary>
    Read = 1 << 0,

    /// <summary>Bit 1.</summary>
    Write = 1 << 1,

    /// <summary>Composite of all defined bits — pins the walker's handling of computed enum values.</summary>
    All = Read | Write,
}
