// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.SourceLink;
using SourceDocParser.Walk;

namespace SourceDocParser.Tests;

/// <summary>
/// Pins the walker on attribute constructor uid capture: the bound
/// constructor's documentation comment ID flows from Roslyn into
/// <see cref="ApiAttribute.ConstructorUid"/> so docfx can emit the
/// matching <c>ctor:</c> field. Two attribute usages with different
/// constructors on the same attribute type must produce distinct uids.
/// </summary>
public class AttributeConstructorUidWalkerTests
{
    /// <summary>A no-arg attribute usage captures the parameterless constructor uid.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task NoArgAttributeCapturesParameterlessCtorUid()
    {
        var compilation = WalkerTestFixtures.Compile(
            """
            namespace My
            {
                [System.Serializable]
                public class Target { }
            }
            """);
        var walker = new SymbolWalker();
        var resolver = new NullSourceLinkResolver();

        var catalog = walker.Walk("net10.0", compilation.Assembly, compilation, resolver);
        var target = catalog.Types.OfType<ApiObjectType>().Single(t => t.Name == "Target");
        var serializable = target.Attributes.Single(a => a.DisplayName == "Serializable");

        await Assert.That(serializable.ConstructorUid).IsEqualTo("M:System.SerializableAttribute.#ctor");
    }

    /// <summary>An attribute usage with a positional argument captures the matching parameterised ctor uid.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ParameterisedAttributeCapturesBoundCtorUid()
    {
        var compilation = WalkerTestFixtures.Compile(
            """
            namespace My
            {
                [System.ComponentModel.Browsable(false)]
                public class Target { }
            }
            """);
        var walker = new SymbolWalker();
        var resolver = new NullSourceLinkResolver();

        var catalog = walker.Walk("net10.0", compilation.Assembly, compilation, resolver);
        var target = catalog.Types.OfType<ApiObjectType>().Single(t => t.Name == "Target");
        var browsable = target.Attributes.Single(a => a.DisplayName == "Browsable");

        await Assert.That(browsable.ConstructorUid)
            .IsEqualTo("M:System.ComponentModel.BrowsableAttribute.#ctor(System.Boolean)");
    }

    /// <summary>SourceLink resolver that returns null for every symbol — keeps tests free of a real PDB dependency.</summary>
    private sealed class NullSourceLinkResolver : ISourceLinkResolver
    {
        /// <inheritdoc />
        public string? Resolve(Microsoft.CodeAnalysis.ISymbol symbol) => null;

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
