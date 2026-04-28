// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Common.Tests;

/// <summary>
/// Pins <see cref="CompilerGeneratedNames.IsCompilerGenerated(string)"/>
/// -- the angle-bracket heuristic both emitter packages share for
/// dropping mangled metadata names. Both the string and span overloads
/// must agree.
/// </summary>
public class CompilerGeneratedNamesTests
{
    /// <summary>Plain identifiers without angle brackets are not flagged.</summary>
    /// <param name="name">Identifier under test.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("Foo")]
    [Arguments("MyClass")]
    [Arguments(".ctor")]
    [Arguments("get_Property")]
    public async Task PlainNamesAreNotCompilerGenerated(string name)
    {
        await Assert.That(CompilerGeneratedNames.IsCompilerGenerated(name)).IsFalse();
        await Assert.That(CompilerGeneratedNames.IsCompilerGenerated(name.AsSpan())).IsFalse();
    }

    /// <summary>Mangled identifiers with angle brackets are flagged regardless of position.</summary>
    /// <param name="name">Identifier under test.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("<MyDisplayClass>")]
    [Arguments("<>c__DisplayClass0_0")]
    [Arguments("<MoveNext>d__1")]
    [Arguments("<>9__0_0")]
    [Arguments("Foo<int>")]
    public async Task MangledNamesAreCompilerGenerated(string name)
    {
        await Assert.That(CompilerGeneratedNames.IsCompilerGenerated(name)).IsTrue();
        await Assert.That(CompilerGeneratedNames.IsCompilerGenerated(name.AsSpan())).IsTrue();
    }

    /// <summary>Empty input is never flagged.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmptyNameIsNotCompilerGenerated()
    {
        await Assert.That(CompilerGeneratedNames.IsCompilerGenerated(string.Empty)).IsFalse();
        await Assert.That(CompilerGeneratedNames.IsCompilerGenerated([])).IsFalse();
    }
}
