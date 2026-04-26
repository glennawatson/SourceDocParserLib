// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;

namespace SourceDocParser.Tests;

/// <summary>
/// Tests for <see cref="CompilationDiagnosticsExtensions.ReportDiagnostics"/>.
/// </summary>
public class CompilationDiagnosticsExtensionsTests
{
    /// <summary>Gets the runtime BCL assemblies as <see cref="MetadataReference"/>s so synthetic compilations resolve System.Object.</summary>
    private static List<MetadataReference> BclReferences { get; } =
    [
        .. AppDomain.CurrentDomain.GetAssemblies()
            .Where(static a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(static a => MetadataReference.CreateFromFile(a.Location)),
    ];

    /// <summary>
    /// A compilation with no errors returns false.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReportDiagnosticsReturnsFalseWhenNoErrors()
    {
        var compilation = CSharpCompilation.Create(
            "Clean",
            [CSharpSyntaxTree.ParseText("public class A { }")],
            BclReferences);

        var hasErrors = compilation.ReportDiagnostics(NullLogger.Instance);

        await Assert.That(hasErrors).IsFalse();
    }

    /// <summary>
    /// A compilation with declaration errors returns true.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReportDiagnosticsReturnsTrueWhenErrorsPresent()
    {
        // Duplicate type with the same name in the same namespace -> declaration error.
        var compilation = CSharpCompilation.Create(
            "Broken",
            [
                CSharpSyntaxTree.ParseText("public class Dup { }"),
                CSharpSyntaxTree.ParseText("public class Dup { }"),
            ],
            BclReferences);

        var hasErrors = compilation.ReportDiagnostics(NullLogger.Instance);

        await Assert.That(hasErrors).IsTrue();
    }

    /// <summary>
    /// Null arguments throw before any work.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReportDiagnosticsValidatesArguments()
    {
        var compilation = CSharpCompilation.Create("X");

        await Assert.That(() => CompilationDiagnosticsExtensions.ReportDiagnostics(null!, NullLogger.Instance))
            .Throws<ArgumentNullException>();
        await Assert.That(() => compilation.ReportDiagnostics(null!))
            .Throws<ArgumentNullException>();
    }
}
