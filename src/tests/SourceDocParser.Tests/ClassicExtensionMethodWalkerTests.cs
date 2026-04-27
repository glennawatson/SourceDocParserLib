// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.SourceLink;
using SourceDocParser.Walk;

namespace SourceDocParser.Tests;

/// <summary>
/// Pins the walker on the classic <c>static void Foo(this T self, ...)</c>
/// extension-method shape so a regression in IsExtension detection or
/// receiver-parameter capture surfaces immediately. The C# 14
/// extension-block path has its own coverage in
/// <see cref="CSharp14ExtensionWalkerTests"/>; this fixture stays
/// scoped to the long-standing classic syntax that's been in the
/// language since C# 3.
/// </summary>
public class ClassicExtensionMethodWalkerTests
{
    /// <summary>A classic <c>this T</c> extension method on a static class is captured with IsExtension + receiver parameter.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ClassicExtensionMethodCapturedWithReceiver()
    {
        var compilation = WalkerTestFixtures.Compile(
            """
            namespace My
            {
                public class Target { }

                public static class Helpers
                {
                    public static void DoIt(this Target self, int count) { }
                }
            }
            """);
        var walker = new SymbolWalker();
        var resolver = new NullSourceLinkResolver();

        var catalog = walker.Walk("net10.0", compilation.Assembly, compilation, resolver);

        var helpers = catalog.Types.OfType<ApiObjectType>().Single(t => t.Name == "Helpers");
        await Assert.That(helpers.IsStatic).IsTrue();

        var doIt = helpers.Members.Single(m => m.Name == "DoIt");
        await Assert.That(doIt.IsExtension).IsTrue();
        await Assert.That(doIt.Parameters.Length).IsEqualTo(2);
        await Assert.That(doIt.Parameters[0].Name).IsEqualTo("self");
        await Assert.That(doIt.Parameters[0].Type.DisplayName).Contains("Target");
        await Assert.That(doIt.Parameters[0].Type.Uid).Contains("Target");
    }

    /// <summary>Non-extension methods on the same static host stay flagged as IsExtension=false.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task NonExtensionStaticMethodIsNotFlagged()
    {
        var compilation = WalkerTestFixtures.Compile(
            """
            namespace My
            {
                public static class Helpers
                {
                    public static void Plain() { }
                }
            }
            """);
        var walker = new SymbolWalker();
        var resolver = new NullSourceLinkResolver();

        var catalog = walker.Walk("net10.0", compilation.Assembly, compilation, resolver);
        var helpers = catalog.Types.OfType<ApiObjectType>().Single(t => t.Name == "Helpers");
        var plain = helpers.Members.Single(m => m.Name == "Plain");

        await Assert.That(plain.IsExtension).IsFalse();
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
