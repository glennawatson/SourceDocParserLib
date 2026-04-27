// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Common.Tests;

/// <summary>
/// Pins the shared denylist + allowlist used by every emitter to
/// decide which attributes survive into rendered output. The rule
/// set mirrors docfx's <c>defaultfilterconfig.yml</c>: the
/// <c>System.Runtime.CompilerServices</c> namespace is dropped except
/// for <c>ExtensionAttribute</c>.
/// </summary>
public class AttributeFilterRulesTests
{
    /// <summary>Compiler-emitted markers under <c>System.Runtime.CompilerServices</c> are excluded.</summary>
    /// <param name="uid">Attribute documentation comment ID.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("T:System.Runtime.CompilerServices.NullableContextAttribute")]
    [Arguments("T:System.Runtime.CompilerServices.IsReadOnlyAttribute")]
    [Arguments("T:System.Runtime.CompilerServices.RefSafetyRulesAttribute")]
    [Arguments("T:System.Runtime.CompilerServices.CompilerGeneratedAttribute")]
    public async Task CompilerServicesAttributesAreExcluded(string uid) => await Assert.That(AttributeFilterRules.IsExcluded(uid)).IsTrue();

    /// <summary><c>ExtensionAttribute</c> is allowlisted despite being in the denylisted namespace.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtensionAttributeIsAllowlisted() =>
        await Assert.That(
                AttributeFilterRules.IsExcluded("T:System.Runtime.CompilerServices.ExtensionAttribute"))
            .IsFalse();

    /// <summary>Attributes outside the denylisted namespace pass through.</summary>
    /// <param name="uid">Attribute documentation comment ID.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("T:System.SerializableAttribute")]
    [Arguments("T:System.FlagsAttribute")]
    [Arguments("T:System.ObsoleteAttribute")]
    [Arguments("T:My.Custom.MyAttribute")]
    public async Task UserAttributesArePassedThrough(string uid) => await Assert.That(AttributeFilterRules.IsExcluded(uid)).IsFalse();

    /// <summary>Inputs without a Roslyn prefix still go through the namespace match.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task UnprefixedUidsStillMatchDenylist() =>
        await Assert.That(
                AttributeFilterRules.IsExcluded("System.Runtime.CompilerServices.NullableContextAttribute"))
            .IsTrue();
}
