// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using BenchmarkDotNet.Attributes;

namespace SourceDocParser.Benchmarks;

/// <summary>
/// Micro-benchmarks for <see cref="XmlDocToMarkdown.Convert(string)"/> — the
/// conversion runs once per documented symbol, so it sits on the hot
/// path for thousands of pages per build.
/// </summary>
[MemoryDiagnoser]
public class XmlDocToMarkdownBenchmarks
{
    /// <summary>A typical short summary fragment (one sentence, no markup).</summary>
    private const string PlainSummary = "Returns the resolved package version, or null when no stable release exists.";

    /// <summary>A typical method summary with see/paramref/c markup.</summary>
    private const string TaggedSummary =
        "When <paramref name=\"value\"/> is <see langword=\"null\"/> the call delegates to " +
        "<see cref=\"M:System.String.Empty\"/> via <c>Foo()</c> and falls back to <see href=\"https://example.com\">the docs</see>.";

    /// <summary>A summary containing a fenced code block plus a bullet list.</summary>
    private const string CodeAndListSummary = """
        Use the API as follows:
        <code>var result = await client.GetAsync(url);</code>
        Notes:
        <list type="bullet">
          <item><description>Caches via the resilience pipeline.</description></item>
          <item><description>Retries 6× with exponential backoff.</description></item>
          <item><description>Honours the supplied cancellation token.</description></item>
        </list>
        """;

    /// <summary>Converter under test — class is stateless so one instance is reused across iterations.</summary>
    private readonly XmlDocToMarkdown _converter = new();

    /// <summary>Conversion of a plain summary fragment with no markup.</summary>
    /// <returns>The converted markdown.</returns>
    [Benchmark(Baseline = true)]
    public string ConvertPlainSummary() => _converter.Convert(PlainSummary);

    /// <summary>Conversion of a typical method summary with see / paramref / c markup.</summary>
    /// <returns>The converted markdown.</returns>
    [Benchmark]
    public string ConvertTaggedSummary() => _converter.Convert(TaggedSummary);

    /// <summary>Conversion of a summary containing a fenced code block + bullet list.</summary>
    /// <returns>The converted markdown.</returns>
    [Benchmark]
    public string ConvertCodeAndListSummary() => _converter.Convert(CodeAndListSummary);
}
