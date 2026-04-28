// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Zensical.Navigation;

/// <summary>
/// Coarse type-kind label exposed on every <see cref="NavigationEntry"/>
/// so consumers can decorate sidebar entries with a Microsoft-Learn-
/// style suffix or icon (e.g. <c>"Change&lt;T&gt; Class"</c> /
/// <c>"Change&lt;T1,T2&gt; Delegate"</c>) and tell visually-identical
/// names apart at a glance. Pre-baking the suffix into
/// <see cref="NavigationEntry.Title"/> would force a single
/// presentation; surfacing the raw kind lets the consumer localise,
/// re-skin, or omit it freely.
/// </summary>
public enum NavigationTypeKind
{
    /// <summary>A non-record reference type (<c>class</c>).</summary>
    Class,

    /// <summary>A non-record value type (<c>struct</c>).</summary>
    Struct,

    /// <summary>An interface type.</summary>
    Interface,

    /// <summary>A reference-type record (<c>record</c> / <c>record class</c>).</summary>
    Record,

    /// <summary>A value-type record (<c>record struct</c>).</summary>
    RecordStruct,

    /// <summary>An enum type.</summary>
    Enum,

    /// <summary>A delegate type.</summary>
    Delegate,

    /// <summary>A C# 15+ closed discriminated-union base type.</summary>
    Union,
}
