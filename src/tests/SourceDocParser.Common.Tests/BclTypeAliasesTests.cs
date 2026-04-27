// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Common.Tests;

/// <summary>
/// Pins <see cref="BclTypeAliases.ToKeyword"/> and
/// <see cref="BclTypeAliases.ToClr"/> on the round-trip mapping
/// emitters depend on for primitive reference labels and
/// reverse-lifted UID synthesis.
/// </summary>
public class BclTypeAliasesTests
{
    /// <summary>Every BCL primitive lowers to its C# keyword form.</summary>
    /// <param name="bareName">CLR full name.</param>
    /// <param name="expected">The expected keyword.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("System.Object", "object")]
    [Arguments("System.String", "string")]
    [Arguments("System.Boolean", "bool")]
    [Arguments("System.Int32", "int")]
    [Arguments("System.UInt32", "uint")]
    [Arguments("System.Int64", "long")]
    [Arguments("System.UInt64", "ulong")]
    [Arguments("System.Int16", "short")]
    [Arguments("System.UInt16", "ushort")]
    [Arguments("System.Byte", "byte")]
    [Arguments("System.SByte", "sbyte")]
    [Arguments("System.Char", "char")]
    [Arguments("System.Double", "double")]
    [Arguments("System.Single", "float")]
    [Arguments("System.Decimal", "decimal")]
    [Arguments("System.Void", "void")]
    public async Task ToKeywordRewritesKnownPrimitive(string bareName, string expected)
    {
        var result = BclTypeAliases.ToKeyword(bareName, fallback: "FALLBACK");

        await Assert.That(result).IsEqualTo(expected);
    }

    /// <summary>Unknown bare names pass through to the supplied fallback display.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ToKeywordReturnsFallbackForUnknown()
    {
        var result = BclTypeAliases.ToKeyword("ReactiveUI.ReactiveObject", fallback: "ReactiveObject");

        await Assert.That(result).IsEqualTo("ReactiveObject");
    }

    /// <summary>Every C# keyword promotes back to the matching CLR full name.</summary>
    /// <param name="keyword">C# keyword.</param>
    /// <param name="expected">The expected CLR full name.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("object", "System.Object")]
    [Arguments("string", "System.String")]
    [Arguments("bool", "System.Boolean")]
    [Arguments("int", "System.Int32")]
    [Arguments("uint", "System.UInt32")]
    [Arguments("long", "System.Int64")]
    [Arguments("ulong", "System.UInt64")]
    [Arguments("short", "System.Int16")]
    [Arguments("ushort", "System.UInt16")]
    [Arguments("byte", "System.Byte")]
    [Arguments("sbyte", "System.SByte")]
    [Arguments("char", "System.Char")]
    [Arguments("double", "System.Double")]
    [Arguments("float", "System.Single")]
    [Arguments("decimal", "System.Decimal")]
    [Arguments("void", "System.Void")]
    public async Task ToClrLiftsKeywordToFullName(string keyword, string expected)
    {
        var result = BclTypeAliases.ToClr(keyword);

        await Assert.That(result).IsEqualTo(expected);
    }

    /// <summary>Non-keyword names pass through unchanged.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ToClrPassesThroughUnknown()
    {
        var result = BclTypeAliases.ToClr("ReactiveObject");

        await Assert.That(result).IsEqualTo("ReactiveObject");
    }
}
