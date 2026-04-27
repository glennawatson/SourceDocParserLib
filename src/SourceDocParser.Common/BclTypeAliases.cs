// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Common;

/// <summary>
/// Two-way map between the C# language keywords for the BCL
/// primitive types (<c>bool</c>, <c>int</c>, <c>string</c>, …) and
/// their CLR full names (<c>System.Boolean</c>, <c>System.Int32</c>,
/// <c>System.String</c>, …). Emitters use <see cref="ToKeyword"/>
/// when rendering reference labels so the output matches the form
/// docfx and the C# specification produce; the inverse
/// <see cref="ToClr"/> path lifts a keyword back to the CLR name
/// when synthesising a UID from a display name.
/// </summary>
public static class BclTypeAliases
{
    /// <summary>
    /// Returns the C# keyword alias for <paramref name="bareName"/>
    /// when it names one of the well-known BCL primitive types,
    /// otherwise returns <paramref name="fallback"/>. Use
    /// <paramref name="bareName"/> as the lookup key (e.g.
    /// <c>System.Object</c>) and <paramref name="fallback"/> as the
    /// caller's existing display label so non-primitive references
    /// pass through unchanged.
    /// </summary>
    /// <param name="bareName">Reference name without any UID prefix (e.g. <c>System.Object</c>).</param>
    /// <param name="fallback">Display string to return when the name isn't a known primitive.</param>
    /// <returns>The keyword alias, or <paramref name="fallback"/>.</returns>
    public static string ToKeyword(string bareName, string fallback) => bareName switch
    {
        "System.Object" => "object",
        "System.String" => "string",
        "System.Boolean" => "bool",
        "System.Int32" => "int",
        "System.UInt32" => "uint",
        "System.Int64" => "long",
        "System.UInt64" => "ulong",
        "System.Int16" => "short",
        "System.UInt16" => "ushort",
        "System.Byte" => "byte",
        "System.SByte" => "sbyte",
        "System.Char" => "char",
        "System.Double" => "double",
        "System.Single" => "float",
        "System.Decimal" => "decimal",
        "System.Void" => "void",
        _ => fallback,
    };

    /// <summary>
    /// Inverse of <see cref="ToKeyword"/> — promotes a C# keyword
    /// alias to its CLR full name when one applies, otherwise
    /// returns <paramref name="name"/> unchanged. Used when
    /// synthesising a CLR UID from a display token (e.g. spec
    /// argument lifting in generic type references).
    /// </summary>
    /// <param name="name">A possibly-aliased type name (e.g. <c>int</c>).</param>
    /// <returns>The promoted CLR name, or the input unchanged.</returns>
    public static string ToClr(string name) => name switch
    {
        "int" => "System.Int32",
        "uint" => "System.UInt32",
        "long" => "System.Int64",
        "ulong" => "System.UInt64",
        "short" => "System.Int16",
        "ushort" => "System.UInt16",
        "byte" => "System.Byte",
        "sbyte" => "System.SByte",
        "bool" => "System.Boolean",
        "char" => "System.Char",
        "string" => "System.String",
        "object" => "System.Object",
        "double" => "System.Double",
        "float" => "System.Single",
        "decimal" => "System.Decimal",
        "void" => "System.Void",
        _ => name,
    };
}
