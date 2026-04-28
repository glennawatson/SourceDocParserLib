// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.XmlDoc;

namespace SourceDocParser.Tests;

/// <summary>
/// Pins <see cref="ApiDocumentationExtensions.RenderWith(ApiDocumentation, XmlDocToMarkdown)"/> --
/// the per-render adapter that converts every text-bearing field of an
/// <see cref="ApiDocumentation"/> through the supplied <see cref="XmlDocToMarkdown"/>.
/// Covers the null-guards, the <see cref="ApiDocumentation.Empty"/> fast path,
/// the all-blank fast path, the per-field copy semantics, and the entry-key
/// pass-through behaviour for parameter / type-parameter / exception lists.
/// </summary>
public class ApiDocumentationExtensionsTests
{
    /// <summary>Null doc throws ArgumentNullException.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderWithThrowsWhenDocIsNull()
    {
        var converter = new XmlDocToMarkdown();
        await Assert.That(() => ApiDocumentationExtensions.RenderWith(null!, converter))
            .Throws<ArgumentNullException>();
    }

    /// <summary>Null converter throws ArgumentNullException.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderWithThrowsWhenConverterIsNull() =>
        await Assert.That(() => ApiDocumentation.Empty.RenderWith(null!))
            .Throws<ArgumentNullException>();

    /// <summary>The shared <see cref="ApiDocumentation.Empty"/> singleton short-circuits and is returned by reference.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderWithReturnsSameInstanceForEmptySingleton()
    {
        var converter = new XmlDocToMarkdown();
        var result = ApiDocumentation.Empty.RenderWith(converter);
        await Assert.That(result).IsSameReferenceAs(ApiDocumentation.Empty);
    }

    /// <summary>An all-blank doc that isn't the singleton still skips the rebuild via the per-field probe.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderWithReturnsSameInstanceForAllBlankDoc()
    {
        var blank = new ApiDocumentation(
            Summary: string.Empty,
            Remarks: string.Empty,
            Returns: string.Empty,
            Value: string.Empty,
            Examples: [],
            Parameters: [],
            TypeParameters: [],
            Exceptions: [],
            SeeAlso: ["T:System.String"],
            InheritedFrom: "Object.ToString");
        var converter = new XmlDocToMarkdown();

        var result = blank.RenderWith(converter);

        await Assert.That(result).IsSameReferenceAs(blank);
    }

    /// <summary>Every text-shaped scalar field is run through the converter.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderWithConvertsScalarTextFields()
    {
        var doc = new ApiDocumentation(
            Summary: "summary text",
            Remarks: "remarks text",
            Returns: "returns text",
            Value: "value text",
            Examples: [],
            Parameters: [],
            TypeParameters: [],
            Exceptions: [],
            SeeAlso: [],
            InheritedFrom: null);
        var converter = new XmlDocToMarkdown();

        var result = doc.RenderWith(converter);

        await Assert.That(result).IsNotSameReferenceAs(doc);
        await Assert.That(result.Summary).IsEqualTo("summary text");
        await Assert.That(result.Remarks).IsEqualTo("remarks text");
        await Assert.That(result.Returns).IsEqualTo("returns text");
        await Assert.That(result.Value).IsEqualTo("value text");
    }

    /// <summary>XML entities in scalar fields decode via the converter (proves Convert was actually invoked).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderWithDecodesEntitiesViaConverter()
    {
        var doc = new ApiDocumentation(
            Summary: "a &amp; b",
            Remarks: string.Empty,
            Returns: string.Empty,
            Value: string.Empty,
            Examples: [],
            Parameters: [],
            TypeParameters: [],
            Exceptions: [],
            SeeAlso: [],
            InheritedFrom: null);
        var converter = new XmlDocToMarkdown();

        var result = doc.RenderWith(converter);

        await Assert.That(result.Summary).IsEqualTo("a & b");
    }

    /// <summary>Empty examples array round-trips by reference (no allocation).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderWithKeepsEmptyExamplesArrayByReference()
    {
        var doc = new ApiDocumentation(
            Summary: "non-empty",
            Remarks: string.Empty,
            Returns: string.Empty,
            Value: string.Empty,
            Examples: [],
            Parameters: [],
            TypeParameters: [],
            Exceptions: [],
            SeeAlso: [],
            InheritedFrom: null);
        var converter = new XmlDocToMarkdown();

        var result = doc.RenderWith(converter);

        await Assert.That(result.Examples).IsSameReferenceAs(doc.Examples);
        await Assert.That(result.Parameters).IsSameReferenceAs(doc.Parameters);
        await Assert.That(result.TypeParameters).IsSameReferenceAs(doc.TypeParameters);
        await Assert.That(result.Exceptions).IsSameReferenceAs(doc.Exceptions);
    }

    /// <summary>Each examples entry is converted; the result is a fresh array of the same length.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderWithConvertsEachExampleFragment()
    {
        var doc = new ApiDocumentation(
            Summary: string.Empty,
            Remarks: string.Empty,
            Returns: string.Empty,
            Value: string.Empty,
            Examples: ["first &amp; example", "second example"],
            Parameters: [],
            TypeParameters: [],
            Exceptions: [],
            SeeAlso: [],
            InheritedFrom: null);
        var converter = new XmlDocToMarkdown();

        var result = doc.RenderWith(converter);

        await Assert.That(result.Examples).IsNotSameReferenceAs(doc.Examples);
        await Assert.That(result.Examples.Length).IsEqualTo(2);
        await Assert.That(result.Examples[0]).IsEqualTo("first & example");
        await Assert.That(result.Examples[1]).IsEqualTo("second example");
    }

    /// <summary>DocEntry values are converted; names pass through; the result is a fresh array.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderWithConvertsParameterValuesPreservingNames()
    {
        var parameters = new[]
        {
            new DocEntry("first", "first &amp; description"),
            new DocEntry("second", "second description"),
        };
        var doc = new ApiDocumentation(
            Summary: string.Empty,
            Remarks: string.Empty,
            Returns: string.Empty,
            Value: string.Empty,
            Examples: [],
            Parameters: parameters,
            TypeParameters: [],
            Exceptions: [],
            SeeAlso: [],
            InheritedFrom: null);
        var converter = new XmlDocToMarkdown();

        var result = doc.RenderWith(converter);

        await Assert.That(result.Parameters).IsNotSameReferenceAs(doc.Parameters);
        await Assert.That(result.Parameters.Length).IsEqualTo(2);
        await Assert.That(result.Parameters[0].Name).IsEqualTo("first");
        await Assert.That(result.Parameters[0].Value).IsEqualTo("first & description");
        await Assert.That(result.Parameters[1].Name).IsEqualTo("second");
        await Assert.That(result.Parameters[1].Value).IsEqualTo("second description");
    }

    /// <summary>TypeParameters and Exceptions take the same conversion path as Parameters.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderWithConvertsTypeParametersAndExceptions()
    {
        var doc = new ApiDocumentation(
            Summary: string.Empty,
            Remarks: string.Empty,
            Returns: string.Empty,
            Value: string.Empty,
            Examples: [],
            Parameters: [],
            TypeParameters: [new DocEntry("T", "type param &amp; T")],
            Exceptions: [new DocEntry("T:System.ArgumentNullException", "thrown when null")],
            SeeAlso: [],
            InheritedFrom: null);
        var converter = new XmlDocToMarkdown();

        var result = doc.RenderWith(converter);

        await Assert.That(result.TypeParameters.Length).IsEqualTo(1);
        await Assert.That(result.TypeParameters[0].Name).IsEqualTo("T");
        await Assert.That(result.TypeParameters[0].Value).IsEqualTo("type param & T");
        await Assert.That(result.Exceptions.Length).IsEqualTo(1);
        await Assert.That(result.Exceptions[0].Name).IsEqualTo("T:System.ArgumentNullException");
        await Assert.That(result.Exceptions[0].Value).IsEqualTo("thrown when null");
    }

    /// <summary>SeeAlso (cref UIDs) and InheritedFrom (display name) are passed through unmodified.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderWithLeavesSeeAlsoAndInheritedFromUntouched()
    {
        var seeAlso = new[] { "T:System.String", "M:Foo.Bar" };
        var doc = new ApiDocumentation(
            Summary: "render me",
            Remarks: string.Empty,
            Returns: string.Empty,
            Value: string.Empty,
            Examples: [],
            Parameters: [],
            TypeParameters: [],
            Exceptions: [],
            SeeAlso: seeAlso,
            InheritedFrom: "Object.ToString");
        var converter = new XmlDocToMarkdown();

        var result = doc.RenderWith(converter);

        await Assert.That(result.SeeAlso).IsSameReferenceAs(seeAlso);
        await Assert.That(result.InheritedFrom).IsEqualTo("Object.ToString");
    }
}
