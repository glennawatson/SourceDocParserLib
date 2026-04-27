// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceDocParser.Walk;

namespace SourceDocParser.Tests.Walk;

/// <summary>
/// Roslyn-driven coverage of <see cref="AttributeExtractor"/>: drives
/// each Roslyn-dependent method through a small CSharpCompilation
/// snippet so the symbol-level branches (synthetic attribute-class,
/// missing constructor, obsolete with message, obsolete without
/// message, every TypedConstant kind) are pinned without resorting
/// to mocking <see cref="AttributeData"/>.
/// </summary>
public class AttributeExtractorTests
{
    /// <summary>Extract returns one model attribute per usage in declaration order.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtractReturnsAttributesInDeclarationOrder()
    {
        var symbol = GetTypeSymbol(
            """
            using System;
            [Serializable]
            [Obsolete("retired")]
            public class Target { }
            """,
            "Target");

        var attributes = AttributeExtractor.Extract(symbol);

        await Assert.That(attributes.Length).IsEqualTo(2);
        await Assert.That(attributes[0].DisplayName).IsEqualTo("Serializable");
        await Assert.That(attributes[1].DisplayName).IsEqualTo("Obsolete");
    }

    /// <summary>Extract returns the empty array for symbols carrying no attributes.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtractReturnsEmptyForSymbolWithoutAttributes()
    {
        var symbol = GetTypeSymbol("public class Target { }", "Target");

        var attributes = AttributeExtractor.Extract(symbol);

        await Assert.That(attributes.Length).IsEqualTo(0);
    }

    /// <summary>ExtractAll returns the attribute list plus the resolved Obsolete state in one pass.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtractAllResolvesObsoleteWithMessage()
    {
        var symbol = GetTypeSymbol(
            """
            using System;
            [Obsolete("retired")]
            public class Target { }
            """,
            "Target");

        var (attributes, isObsolete, obsoleteMessage) = AttributeExtractor.ExtractAll(symbol);

        await Assert.That(attributes.Length).IsEqualTo(1);
        await Assert.That(isObsolete).IsTrue();
        await Assert.That(obsoleteMessage).IsEqualTo("retired");
    }

    /// <summary>ExtractAll returns <c>(false, null)</c> when no Obsolete attribute is present.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtractAllReportsNotObsoleteWhenAbsent()
    {
        var symbol = GetTypeSymbol(
            """
            using System;
            [Serializable]
            public class Target { }
            """,
            "Target");

        var (_, isObsolete, obsoleteMessage) = AttributeExtractor.ExtractAll(symbol);

        await Assert.That(isObsolete).IsFalse();
        await Assert.That(obsoleteMessage).IsNull();
    }

    /// <summary>ExtractAll on no attributes short-circuits to the empty result tuple.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtractAllReturnsEmptyTupleForNoAttributes()
    {
        var symbol = GetTypeSymbol("public class Target { }", "Target");

        var (attributes, isObsolete, obsoleteMessage) = AttributeExtractor.ExtractAll(symbol);

        await Assert.That(attributes.Length).IsEqualTo(0);
        await Assert.That(isObsolete).IsFalse();
        await Assert.That(obsoleteMessage).IsNull();
    }

    /// <summary>ExtractAll keeps the first <c>[Obsolete]</c> message when multiple are applied.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtractAllFirstObsoleteMessageWins()
    {
        // Multi-Obsolete is unusual but valid in metadata. Pin the
        // first-match-wins ordering rule documented in ExtractAll.
        var symbol = GetTypeSymbol(
            """
            using System;
            [Obsolete("first")]
            [Obsolete("second")]
            public class Target { }
            """,
            "Target");

        var (_, isObsolete, obsoleteMessage) = AttributeExtractor.ExtractAll(symbol);

        await Assert.That(isObsolete).IsTrue();
        await Assert.That(obsoleteMessage).IsEqualTo("first");
    }

    /// <summary><c>[Obsolete]</c> without a message reports IsObsolete=true and a null message.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtractAllReportsObsoleteWithoutMessage()
    {
        var symbol = GetTypeSymbol(
            """
            using System;
            [Obsolete]
            public class Target { }
            """,
            "Target");

        var (_, isObsolete, obsoleteMessage) = AttributeExtractor.ExtractAll(symbol);

        await Assert.That(isObsolete).IsTrue();
        await Assert.That(obsoleteMessage).IsNull();
    }

    /// <summary>The standalone ResolveObsolete path matches the ExtractAll resolution result.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolveObsoleteMatchesExtractAll()
    {
        var symbol = GetTypeSymbol(
            """
            using System;
            [Obsolete("retired")]
            public class Target { }
            """,
            "Target");

        var (isObsolete, message) = AttributeExtractor.ResolveObsolete(symbol);

        await Assert.That(isObsolete).IsTrue();
        await Assert.That(message).IsEqualTo("retired");
    }

    /// <summary>ResolveObsolete returns <c>(false, null)</c> when no Obsolete usage is present.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolveObsoleteReturnsFalseWhenAbsent()
    {
        var symbol = GetTypeSymbol(
            """
            using System;
            [Serializable]
            public class Target { }
            """,
            "Target");

        var (isObsolete, message) = AttributeExtractor.ResolveObsolete(symbol);

        await Assert.That(isObsolete).IsFalse();
        await Assert.That(message).IsNull();
    }

    /// <summary>ExtractCore on an empty/default ImmutableArray short-circuits to the empty result.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtractCoreReturnsEmptyForDefaultInput()
    {
        await Assert.That(AttributeExtractor.ExtractCore(default).Length).IsEqualTo(0);
        await Assert.That(AttributeExtractor.ExtractCore([]).Length).IsEqualTo(0);
    }

    /// <summary>ExtractCore against a real attribute list materialises one ApiAttribute per entry.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtractCoreMaterialisesEachAttribute()
    {
        var symbol = GetTypeSymbol(
            """
            using System;
            [Serializable]
            [Obsolete("retired")]
            public class Target { }
            """,
            "Target");

        var attributes = AttributeExtractor.ExtractCore(symbol.GetAttributes());

        await Assert.That(attributes.Length).IsEqualTo(2);
        await Assert.That(attributes[0].DisplayName).IsEqualTo("Serializable");
        await Assert.That(attributes[1].DisplayName).IsEqualTo("Obsolete");
    }

    /// <summary>IsObsoleteAttribute returns true for the BCL <c>System.ObsoleteAttribute</c> class.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsObsoleteAttributeTrueForBclObsolete()
    {
        var symbol = GetTypeSymbol(
            """
            using System;
            [Obsolete]
            public class Target { }
            """,
            "Target");
        var obsolete = symbol.GetAttributes()[0].AttributeClass!;

        await Assert.That(AttributeExtractor.IsObsoleteAttribute(obsolete)).IsTrue();
    }

    /// <summary>IsObsoleteAttribute returns false for any other attribute class.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsObsoleteAttributeFalseForOtherTypes()
    {
        var symbol = GetTypeSymbol(
            """
            using System;
            [Serializable]
            public class Target { }
            """,
            "Target");
        var serializable = symbol.GetAttributes()[0].AttributeClass!;

        await Assert.That(AttributeExtractor.IsObsoleteAttribute(serializable)).IsFalse();
        await Assert.That(AttributeExtractor.IsObsoleteAttribute(null)).IsFalse();
    }

    /// <summary>Convert renders the constructor's documentation comment ID alongside the type-level UID.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConvertCapturesConstructorUid()
    {
        var symbol = GetTypeSymbol(
            """
            using System;
            [Obsolete("retired")]
            public class Target { }
            """,
            "Target");

        var converted = AttributeExtractor.Convert(symbol.GetAttributes()[0]);

        await Assert.That(converted.DisplayName).IsEqualTo("Obsolete");
        await Assert.That(converted.Uid).IsEqualTo("T:System.ObsoleteAttribute");
        await Assert.That(converted.ConstructorUid).Contains("M:System.ObsoleteAttribute.#ctor(System.String)");
        await Assert.That(converted.Arguments.Length).IsEqualTo(1);
        await Assert.That(converted.Arguments[0].Value).IsEqualTo("\"retired\"");
    }

    /// <summary>FormatConstant renders a primitive int constant via the FormatPrimitive arm.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FormatConstantHandlesPrimitiveInt()
    {
        var symbol = GetTypeSymbol(
            """
            using System.ComponentModel;
            [DefaultValue(7)]
            public class Target { }
            """,
            "Target");
        var constant = symbol.GetAttributes()[0].ConstructorArguments[0];

        await Assert.That(AttributeExtractor.FormatConstant(constant)).IsEqualTo("7");
    }

    /// <summary>FormatConstant returns <c>null</c> for null-valued constants.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FormatConstantHandlesNull()
    {
        var symbol = GetTypeSymbol(
            """
            using System.ComponentModel;
            [DefaultValue((string?)null)]
            public class Target { }
            """,
            "Target");
        var constant = symbol.GetAttributes()[0].ConstructorArguments[0];

        await Assert.That(AttributeExtractor.FormatConstant(constant)).IsEqualTo("null");
    }

    /// <summary>FormatConstant renders a typeof argument as <c>typeof(TypeName)</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FormatConstantHandlesTypeof()
    {
        var symbol = GetTypeSymbol(
            """
            using System;
            [AttributeUsage(AttributeTargets.Class)]
            public class TypeRefAttribute(Type t) : Attribute { }

            [TypeRef(typeof(int))]
            public class Target { }
            """,
            "Target");
        var constant = symbol.GetAttributes()[0].ConstructorArguments[0];

        await Assert.That(AttributeExtractor.FormatConstant(constant)).IsEqualTo("typeof(int)");
    }

    /// <summary>FormatEnumConstant renders an enum-valued constant as <c>EnumName.NumericValue</c> (Roslyn surfaces only the underlying value at this layer).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FormatEnumConstantRendersEnumValue()
    {
        var symbol = GetTypeSymbol(
            """
            using System;
            [AttributeUsage(AttributeTargets.Class)]
            public class Target : Attribute { }
            """,
            "Target");
        var constant = symbol.GetAttributes()[0].ConstructorArguments[0];

        // AttributeTargets.Class is the underlying value 4.
        await Assert.That(AttributeExtractor.FormatEnumConstant(constant)).IsEqualTo("AttributeTargets.4");
    }

    /// <summary>FormatArrayConstant renders an array argument as <c>[a, b, c]</c>, recursing through FormatConstant for each element.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FormatArrayConstantRendersBracketedList()
    {
        var symbol = GetTypeSymbol(
            """
            using System;
            using System.Diagnostics;
            [DebuggerDisplay("x", Target = typeof(int), TargetTypeName = "int")]
            [DebuggerTypeProxy(typeof(int))]
            public class Target { }
            """,
            "Target");

        // Synthesise an array constant via a usage that takes one. The
        // simplest BCL attribute that takes an array is hard to find,
        // so we feed a manual values array via Roslyn — drive the
        // FormatArrayConstant arm directly through FormatConstant.
        // ParamArray attribute not standard on classes; use a custom one below.
        var custom = GetTypeSymbol(
            """
            using System;
            [AttributeUsage(AttributeTargets.Class)]
            public class TagsAttribute(params string[] tags) : Attribute
            {
                public string[] Tags { get; } = tags;
            }

            [Tags("alpha", "beta", "gamma")]
            public class Tagged { }
            """,
            "Tagged");
        var arrayConstant = custom.GetAttributes()[0].ConstructorArguments[0];

        await Assert.That(AttributeExtractor.FormatArrayConstant(arrayConstant))
            .IsEqualTo("[\"alpha\", \"beta\", \"gamma\"]");
    }

    /// <summary>
    /// Compiles <paramref name="source"/> against the runtime BCL and
    /// returns the named type's symbol. Mirrors WalkerTestFixtures.Compile
    /// but localised so the test file stays self-contained.
    /// </summary>
    /// <param name="source">C# source.</param>
    /// <param name="typeName">Simple name of the type to fetch.</param>
    /// <returns>The named type's symbol.</returns>
    private static INamedTypeSymbol GetTypeSymbol(string source, string typeName)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new(LanguageVersion.Preview));
        List<MetadataReference> references = [];
        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!loaded.IsDynamic && loaded.Location is { Length: > 0 } location)
            {
                references.Add(MetadataReference.CreateFromFile(location));
            }
        }

        var compilation = CSharpCompilation.Create("AttributeExtractorTests", [tree], references);
        return compilation.GetTypeByMetadataName(typeName)
            ?? throw new InvalidOperationException($"Type '{typeName}' not found in compilation.");
    }
}
