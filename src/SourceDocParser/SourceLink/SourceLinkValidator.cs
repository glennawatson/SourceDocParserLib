// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.RateLimiting;

namespace SourceDocParser.SourceLink;

/// <summary>
/// HEAD-checks source URLs to detect broken links.
/// </summary>
/// <remarks>
/// Rate-limited to be friendly to GitHub and other hosts.
/// </remarks>
public sealed partial class SourceLinkValidator : ISourceLinkValidator
{
    /// <summary>
    /// Maximum concurrent in-flight HEAD requests.
    /// </summary>
    private const int MaxConcurrentRequests = 8;

    /// <summary>
    /// Per-request HTTP timeout.
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">When <paramref name="entries"/> is null.</exception>
    public async Task<int> ValidateAsync(List<SourceLinkEntry> entries, bool failOnBroken = false, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        logger ??= NullLogger.Instance;

        if (entries.Count == 0)
        {
            LogNothingToValidate(logger);
            return 0;
        }

        var byFileUrl = GroupByFileUrl(entries);
        LogValidating(logger, byFileUrl.Count, entries.Count);

        var rateLimiter = BuildRateLimiter();
        try
        {
            var pipeline = BuildResiliencePipeline(rateLimiter);

            using var http = new HttpClient { Timeout = RequestTimeout };
            var broken = new ConcurrentBag<BrokenLink>();
            var checkedCount = 0;

            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentRequests };
            await Parallel.ForEachAsync(
                byFileUrl,
                parallelOptions,
                async (kvp, ct) =>
                {
                    Interlocked.Increment(ref checkedCount);
                    var url = kvp.Key;
                    try
                    {
                        using var response = await pipeline.ExecuteAsync(
                            static async (state, token) =>
                            {
                                using var request = new HttpRequestMessage(HttpMethod.Head, state.Url);
                                return await state.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                            },
                            state: (Url: url, Http: http),
                            ct).ConfigureAwait(false);

                        if (!response.IsSuccessStatusCode)
                        {
                            broken.Add(new(url, kvp.Value, $"HTTP {(int)response.StatusCode}"));
                        }
                    }
                    catch (Exception ex)
                    {
                        broken.Add(new(url, kvp.Value, ex.Message));
                    }
                }).ConfigureAwait(false);

            ReportResults(byFileUrl.Count, broken, failOnBroken, logger);
            return broken.Count;
        }
        finally
        {
            await rateLimiter.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Groups entries by file URL to deduplicate HEAD checks.
    /// </summary>
    /// <param name="entries">Entries to group.</param>
    /// <returns>A dictionary mapping file URLs to lists of symbol UIDs.</returns>
    private static Dictionary<string, List<string>> GroupByFileUrl(List<SourceLinkEntry> entries)
    {
        var byFileUrl = new Dictionary<string, List<string>>(entries.Count, StringComparer.Ordinal);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var fileUrl = SourceUrlRewriter.StripAnchor(entry.Url);
            if (!byFileUrl.TryGetValue(fileUrl, out var uids))
            {
                uids = [];
                byFileUrl[fileUrl] = uids;
            }

            uids.Add(entry.Uid);
        }

        return byFileUrl;
    }

    /// <summary>
    /// Builds a token-bucket rate limiter.
    /// </summary>
    /// <returns>A new rate limiter.</returns>
    private static TokenBucketRateLimiter BuildRateLimiter() => new(new()
    {
        TokenLimit = 50,
        TokensPerPeriod = 10,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = int.MaxValue,
        AutoReplenishment = true,
    });

    /// <summary>
    /// Builds a Polly resilience pipeline.
    /// </summary>
    /// <param name="rateLimiter">The rate limiter to use.</param>
    /// <returns>A resilience pipeline.</returns>
    private static ResiliencePipeline<HttpResponseMessage> BuildResiliencePipeline(RateLimiter rateLimiter) =>
        new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new()
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>().Handle<HttpRequestException>(),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(0.5),
                BackoffType = DelayBackoffType.Exponential,
            })
            .AddRateLimiter(new RateLimiterStrategyOptions { RateLimiter = args => rateLimiter.AcquireAsync(1, args.Context.CancellationToken) })
            .Build();

    /// <summary>
    /// Reports validation results.
    /// </summary>
    /// <param name="totalChecked">Total unique URLs checked.</param>
    /// <param name="broken">List of broken links.</param>
    /// <param name="failOnBroken">Whether to throw on failures.</param>
    /// <param name="logger">Logger for the report lines.</param>
    private static void ReportResults(int totalChecked, ConcurrentBag<BrokenLink> broken, bool failOnBroken, ILogger logger)
    {
        if (broken.IsEmpty)
        {
            LogValidationPassed(logger, totalChecked);
            return;
        }

        LogValidationFoundBroken(logger, broken.Count, totalChecked);
        foreach (var entry in broken)
        {
            LogBrokenEntry(logger, entry.Reason, entry.Url, entry.Uids.Count);
        }

        if (!failOnBroken)
        {
            return;
        }

        throw new InvalidOperationException($"Source link validation failed: {broken.Count} broken URL(s).");
    }

    /// <summary>Logs that the validator was invoked with no entries.</summary>
    /// <param name="logger">Target logger.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "No source links to validate.")]
    private static partial void LogNothingToValidate(ILogger logger);

    /// <summary>Logs the start of the HEAD-check sweep.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="urlCount">Unique URLs to check after deduplication.</param>
    /// <param name="entryCount">Original per-symbol entry count.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Validating {UrlCount} unique source URL(s) (collapsed from {EntryCount} per-symbol entries)")]
    private static partial void LogValidating(ILogger logger, int urlCount, int entryCount);

    /// <summary>Logs that every URL responded successfully.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="totalChecked">Total unique URLs checked.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "All {TotalChecked} unique source URL(s) validated successfully")]
    private static partial void LogValidationPassed(ILogger logger, int totalChecked);

    /// <summary>Logs the broken-URL summary header.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="brokenCount">Number of broken URLs.</param>
    /// <param name="totalChecked">Total unique URLs checked.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Found {BrokenCount} broken source URL(s) out of {TotalChecked} checked:")]
    private static partial void LogValidationFoundBroken(ILogger logger, int brokenCount, int totalChecked);

    /// <summary>Logs a single broken URL with the reason and symbol count.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="reason">HTTP status code or exception message.</param>
    /// <param name="url">The broken source URL.</param>
    /// <param name="symbolCount">Number of symbols referencing this URL.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "  {Reason}  {Url}  (referenced by {SymbolCount} symbol(s))")]
    private static partial void LogBrokenEntry(ILogger logger, string reason, string url, int symbolCount);

    /// <summary>
    /// Record for a broken link.
    /// </summary>
    /// <param name="Url">Broken URL.</param>
    /// <param name="Uids">Symbols referencing this URL.</param>
    /// <param name="Reason">Reason for failure.</param>
    private sealed record BrokenLink(string Url, List<string> Uids, string Reason);
}
