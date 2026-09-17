// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace SourceDocParser.AllocReport;

/// <summary>
/// Console entry point for the allocation-report tool. Reads a
/// <c>.nettrace</c> file produced by BenchmarkDotNet's
/// <c>EventPipeProfiler</c> (with <c>GcVerbose</c>), aggregates
/// <c>GCAllocationTick</c> samples, and emits a markdown table of the
/// top-N types and call stacks by sampled bytes. The output is meant to
/// be greppable / pastable so a profile can be evaluated without a GUI.
/// </summary>
internal static class Program
{
    /// <summary>Default number of rows to surface in each table.</summary>
    private const int DefaultTopN = 25;

    /// <summary>The exit code when a trace file is not found.</summary>
    private const int ExitCodeTraceFileNotFound = 2;

    /// <summary>The exit code when no allocation samples are found.</summary>
    private const int ExitCodeNoAllocationSamples = 3;

    /// <summary>The exit code when usage is incorrect.</summary>
    private const int ExitCodeUsageError = 1;

    /// <summary>How deep to walk each managed call stack when bucketing.</summary>
    private const int StackDepth = 6;

    /// <summary>Bits to shift for Gigabytes.</summary>
    private const int GbShift = 30;

    /// <summary>Bits to shift for Megabytes.</summary>
    private const int MbShift = 20;

    /// <summary>Bits to shift for Kilobytes.</summary>
    private const int KbShift = 10;

    /// <summary>Multiplier for percentage calculations.</summary>
    private const double PercentMultiplier = 100.0;

    /// <summary>Console entry point.</summary>
    /// <param name="args">Command-line args: <c>&lt;trace.nettrace> [topN]</c>.</param>
    /// <returns>
    /// 0 on success; <see cref="ExitCodeUsageError"/> on usage error;
    /// <see cref="ExitCodeTraceFileNotFound"/> if the trace path is missing;
    /// <see cref="ExitCodeNoAllocationSamples"/> if no allocation samples were found.
    /// </returns>
    internal static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: SourceDocParser.AllocReport <path-to-trace.nettrace> [topN]");
            return ExitCodeUsageError;
        }

        var tracePath = args[0];
        if (!File.Exists(tracePath))
        {
            Console.Error.WriteLine($"trace file not found: {tracePath}");
            return ExitCodeTraceFileNotFound;
        }

        var topN = args.Length > 1 && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : DefaultTopN;

        var stats = AggregateAllocations(tracePath);
        if (stats.TotalSamples == 0)
        {
            Console.Error.WriteLine(
                "trace contained no GC allocation tick events. Make sure the benchmark was instrumented with "
                + "[EventPipeProfiler(EventPipeProfile.GcVerbose)].");
            return ExitCodeNoAllocationSamples;
        }

        WriteHeader(Console.Out, tracePath, stats);
        WriteTypeTable(Console.Out, stats, topN);
        WriteStackTable(Console.Out, stats, topN);
        return 0;
    }

    /// <summary>Walks the trace, aggregating every <c>GCAllocationTick</c> by type name and by managed call-stack key.</summary>
    /// <param name="tracePath">Absolute path to the <c>.nettrace</c> file.</param>
    /// <returns>The aggregated stats.</returns>
    private static AggregatedStats AggregateAllocations(string tracePath)
    {
        var byType = new Dictionary<string, AllocStat>(StringComparer.Ordinal);
        var byStack = new Dictionary<string, StackStat>(StringComparer.Ordinal);
        long totalBytes = 0;
        long totalSamples = 0;

        // TraceLog needs the .etlx indexed form to expose CallStack data.
        var etlxPath = TraceLog.CreateFromEventPipeDataFile(tracePath);
        try
        {
            using var traceLog = new TraceLog(etlxPath);
            var source = traceLog.Events.GetSource();
            source.Clr.GCAllocationTick += data =>
            {
                var typeName = data.TypeName ?? "<unknown>";
                var bytes = data.AllocationAmount64 > 0 ? data.AllocationAmount64 : data.AllocationAmount;

                ref var typeStat = ref CollectionsMarshal.GetValueRefOrAddDefault(byType, typeName, out _);
                typeStat.Bytes += bytes;
                typeStat.Samples++;
                totalBytes += bytes;
                totalSamples++;

                var callStack = data.CallStack();
                if (callStack is null)
                {
                    return;
                }

                var stackKey = FormatStack(callStack, StackDepth);
                ref var stackStat = ref CollectionsMarshal.GetValueRefOrAddDefault(byStack, stackKey, out _);
                stackStat.Bytes += bytes;
                stackStat.Samples++;
                stackStat.TopType ??= typeName;
            };

            _ = source.Process();
        }
        finally
        {
            // CreateFromEventPipeDataFile writes the etlx next to the
            // input; clean it up so repeat runs don't accumulate copies.
            if (!string.Equals(etlxPath, tracePath, StringComparison.OrdinalIgnoreCase) && File.Exists(etlxPath))
            {
                File.Delete(etlxPath);
            }
        }

        return new(byType, byStack, totalBytes, totalSamples);
    }

    /// <summary>Prints the markdown header + summary line.</summary>
    /// <param name="output">Destination for the markdown report.</param>
    /// <param name="tracePath">Trace file name surfaced in the title.</param>
    /// <param name="stats">Aggregated stats whose totals are printed.</param>
    private static void WriteHeader(TextWriter output, string tracePath, AggregatedStats stats)
    {
        output.WriteLine($"# Allocation Report -- {Path.GetFileName(tracePath)}");
        output.WriteLine();
        output.WriteLine($"- Total sampled allocation bytes: **{FormatBytes(stats.TotalBytes)}**");
        output.WriteLine($"- Total sample events: **{stats.TotalSamples:N0}**");
        output.WriteLine("- GCAllocationTick samples one allocation per ~100 KB allocated; absolute bytes are an estimate, *relative* ranking is accurate.");
        output.WriteLine();
    }

    /// <summary>Prints the top-N types-by-bytes table.</summary>
    /// <param name="output">Destination for the markdown report.</param>
    /// <param name="stats">Aggregated stats to render.</param>
    /// <param name="topN">Maximum rows to emit.</param>
    private static void WriteTypeTable(TextWriter output, AggregatedStats stats, int topN)
    {
        output.WriteLine($"## Top {topN} types by sampled bytes");
        output.WriteLine();
        output.WriteLine("| Type | Sampled bytes | % | Samples |");
        output.WriteLine("|---|---:|---:|---:|");
        var entries = new KeyValuePair<string, AllocStat>[stats.ByType.Count];
        ((ICollection<KeyValuePair<string, AllocStat>>)stats.ByType).CopyTo(entries, 0);
        Array.Sort(entries, AllocStatComparer.Instance);
        var count = Math.Min(topN, entries.Length);
        for (var i = 0; i < count; i++)
        {
            var (type, stat) = entries[i];
            var pct = stats.TotalBytes == 0 ? 0D : stat.Bytes * PercentMultiplier / stats.TotalBytes;
            output.WriteLine($"| `{Escape(type)}` | {FormatBytes(stat.Bytes)} | {pct:F1}% | {stat.Samples:N0} |");
        }

        output.WriteLine();
    }

    /// <summary>Prints the top-N stack-by-bytes table.</summary>
    /// <param name="output">Destination for the markdown report.</param>
    /// <param name="stats">Aggregated stats to render.</param>
    /// <param name="topN">Maximum rows to emit.</param>
    private static void WriteStackTable(TextWriter output, AggregatedStats stats, int topN)
    {
        output.WriteLine($"## Top {topN} call stacks by sampled bytes (depth {StackDepth})");
        output.WriteLine();
        output.WriteLine("| Top type | Sampled bytes | % | Samples | Stack |");
        output.WriteLine("|---|---:|---:|---:|---|");
        var entries = new KeyValuePair<string, StackStat>[stats.ByStack.Count];
        ((ICollection<KeyValuePair<string, StackStat>>)stats.ByStack).CopyTo(entries, 0);
        Array.Sort(entries, StackStatComparer.Instance);
        var count = Math.Min(topN, entries.Length);
        for (var i = 0; i < count; i++)
        {
            var (stack, stat) = entries[i];
            var pct = stats.TotalBytes == 0 ? 0D : stat.Bytes * PercentMultiplier / stats.TotalBytes;
            output.WriteLine($"| `{Escape(stat.TopType ?? "<n/a>")}` | {FormatBytes(stat.Bytes)} | {pct:F1}% | {stat.Samples:N0} | {Escape(stack)} |");
        }
    }

    /// <summary>Formats <paramref name="bytes"/> as a human-readable size string.</summary>
    /// <param name="bytes">Byte count.</param>
    /// <returns>Formatted "1.23 MB" / "456 B" string.</returns>
    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << GbShift => $"{bytes / (double)(1L << GbShift):F2} GB",
        >= 1L << MbShift => $"{bytes / (double)(1L << MbShift):F2} MB",
        >= 1L << KbShift => $"{bytes / (double)(1L << KbShift):F2} KB",
        _ => $"{bytes} B",
    };

    /// <summary>
    /// Walks <paramref name="stack"/> top-down (callee-first) and
    /// formats it as a single line where each frame is separated from
    /// its caller by a left-arrow joiner, truncated to
    /// <paramref name="depth"/>.
    /// </summary>
    /// <param name="stack">Stack to render.</param>
    /// <param name="depth">Maximum frame count to include.</param>
    /// <returns>The formatted stack line.</returns>
    private static string FormatStack(TraceCallStack stack, int depth)
    {
        var frames = new List<string>(depth);
        var current = stack;
        while (current is not null && frames.Count < depth)
        {
            var name = current.CodeAddress?.FullMethodName;
            if (!string.IsNullOrEmpty(name))
            {
                frames.Add(ShortenFrame(name));
            }

            current = current.Caller;
        }

        return frames.Count == 0 ? "<no frames>" : string.Join(" <- ", frames);
    }

    /// <summary>Drops the parameter list from a frame name so the table stays scannable.</summary>
    /// <param name="fullName">Method name of the form <c>Type.Method(Params)</c>.</param>
    /// <returns>The shortened frame name.</returns>
    private static string ShortenFrame(string fullName)
    {
        var paren = fullName.IndexOf('(', StringComparison.Ordinal);
        return paren > 0 ? fullName[..paren] : fullName;
    }

    /// <summary>Escapes a value so it's safe to drop into a markdown table cell.</summary>
    /// <param name="value">Raw text.</param>
    /// <returns>Escaped text.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string Escape(string value) => value
        .Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal);

    /// <summary>Per-type allocation accumulator (mutable struct held in dictionary).</summary>
    private record struct AllocStat
    {
        /// <summary>Gets or sets total sampled bytes attributed to this type.</summary>
        public long Bytes { get; set; }

        /// <summary>Gets or sets the number of GCAllocationTick samples that named this type.</summary>
        public long Samples { get; set; }
    }

    /// <summary>Per-stack allocation accumulator (mutable struct held in dictionary).</summary>
    private record struct StackStat
    {
        /// <summary>Gets or sets total sampled bytes attributed to this stack.</summary>
        public long Bytes { get; set; }

        /// <summary>Gets or sets the number of samples that landed on this stack.</summary>
        public long Samples { get; set; }

        /// <summary>Gets or sets the first allocated type seen on this stack, used as a label in the report.</summary>
        public string? TopType { get; set; }
    }

    /// <summary>Orders type allocations by sampled bytes, then type name.</summary>
    private sealed class AllocStatComparer : IComparer<KeyValuePair<string, AllocStat>>
    {
        /// <summary>Gets the shared comparer.</summary>
        public static AllocStatComparer Instance { get; } = new();

        /// <inheritdoc/>
        public int Compare(KeyValuePair<string, AllocStat> x, KeyValuePair<string, AllocStat> y)
        {
            var bytes = y.Value.Bytes.CompareTo(x.Value.Bytes);
            return bytes is 0 ? StringComparer.Ordinal.Compare(x.Key, y.Key) : bytes;
        }
    }

    /// <summary>Orders stack allocations by sampled bytes, then stack name.</summary>
    private sealed class StackStatComparer : IComparer<KeyValuePair<string, StackStat>>
    {
        /// <summary>Gets the shared comparer.</summary>
        public static StackStatComparer Instance { get; } = new();

        /// <inheritdoc/>
        public int Compare(KeyValuePair<string, StackStat> x, KeyValuePair<string, StackStat> y)
        {
            var bytes = y.Value.Bytes.CompareTo(x.Value.Bytes);
            return bytes is 0 ? StringComparer.Ordinal.Compare(x.Key, y.Key) : bytes;
        }
    }

    /// <summary>
    /// Result of the trace pass. Carries the per-type and per-stack
    /// dictionaries plus the summed totals so the renderers don't need
    /// to re-iterate.
    /// </summary>
    /// <param name="ByType">Per-type allocation accumulators.</param>
    /// <param name="ByStack">Per-stack allocation accumulators (stack key -> stat).</param>
    /// <param name="TotalBytes">Sum of every sampled allocation's byte count.</param>
    /// <param name="TotalSamples">Total number of GCAllocationTick samples observed.</param>
    private sealed record AggregatedStats(
        Dictionary<string, AllocStat> ByType,
        Dictionary<string, StackStat> ByStack,
        long TotalBytes,
        long TotalSamples);
}
