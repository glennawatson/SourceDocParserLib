// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Model;

/// <summary>
/// Roslyn member kinds we surface in the docs. Mapped from
/// ISymbol kind/declaration shape; everything we don't document
/// (NamespaceSymbol, NetModule, ErrorType, etc.) just isn't here.
/// </summary>
public enum ApiMemberKind
{
    /// <summary>An instance or static constructor.</summary>
    Constructor,

    /// <summary>A property (read-only, write-only, or read/write).</summary>
    Property,

    /// <summary>A field (instance, static, const, readonly).</summary>
    Field,

    /// <summary>A method (instance, static, extension).</summary>
    Method,

    /// <summary>An operator overload (op_*) or conversion.</summary>
    Operator,

    /// <summary>An event.</summary>
    Event,

    /// <summary>An enum value (as a member of the enclosing enum type).</summary>
    EnumValue,
}
