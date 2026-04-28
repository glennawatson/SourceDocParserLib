// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SamplePdb;

/// <summary>
/// Plain enum with three members + an explicit underlying value on
/// one of them -- exercises the walker's <c>TypeKind.Enum</c> branch
/// and per-member EnumValue capture.
/// </summary>
public enum SampleSeverity
{
    /// <summary>Default level.</summary>
    Info = 0,

    /// <summary>Warning level.</summary>
    Warning = 1,

    /// <summary>Error level.</summary>
    Error = 2,
}
