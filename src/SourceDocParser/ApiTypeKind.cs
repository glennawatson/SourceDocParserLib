// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

/// <summary>
/// Roslyn TypeKind narrowed to the categories docs care about.
/// Anything that should never reach a documented page (Module,
/// Pointer, Submission, Error) just isn't represented.
/// </summary>
public enum ApiTypeKind
{
    /// <summary>A class.</summary>
    Class,

    /// <summary>A struct.</summary>
    Struct,

    /// <summary>An interface.</summary>
    Interface,

    /// <summary>An enum.</summary>
    Enum,

    /// <summary>A delegate.</summary>
    Delegate,

    /// <summary>A record class (a class declared with the record keyword).</summary>
    Record,

    /// <summary>A record struct.</summary>
    RecordStruct,

    /// <summary>
    /// A union type (C# 15+). Reserved for future support — Roslyn 5.x
    /// doesn't yet emit a distinct TypeKind for these, so the walker
    /// won't produce this value today. Defined now so the emitter
    /// surface can be wired in advance.
    /// </summary>
    Union,
}
