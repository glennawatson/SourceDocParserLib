// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

/// <summary>
/// Concrete kind discriminator for object-shaped types
/// (anything carrying a list of members: classes, structs,
/// interfaces, records). Read off <see cref="ApiObjectType.Kind"/>
/// when the same emitter behaviour applies to the whole family
/// and only a label needs to differ.
/// </summary>
public enum ApiObjectKind
{
    /// <summary>A class.</summary>
    Class,

    /// <summary>A struct.</summary>
    Struct,

    /// <summary>An interface.</summary>
    Interface,

    /// <summary>A record class.</summary>
    Record,

    /// <summary>A record struct.</summary>
    RecordStruct,
}
