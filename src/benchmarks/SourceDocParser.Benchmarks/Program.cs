// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using BenchmarkDotNet.Running;

namespace SourceDocParser.Benchmarks;

/// <summary>
/// BenchmarkDotNet entry point. Forwards <c>args</c> to the switcher so
/// callers can filter / pick benchmarks via the standard CLI flags.
/// </summary>
public static class Program
{
    /// <summary>
    /// BenchmarkDotNet entry point.
    /// </summary>
    /// <param name="args">Command-line arguments forwarded to <see cref="BenchmarkSwitcher"/>.</param>
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
