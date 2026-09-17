// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;

namespace SourceDocParser.SourceLink;

/// <summary>HEAD-checks source URLs to detect broken links.</summary>
/// <remarks>
/// Rate-limited to be friendly to GitHub and other hosts.
/// </remarks>
public sealed partial class SourceLinkValidator : ISourceLinkValidator
{
    /// <summary>Maximum concurrent in-flight HEAD requests.</summary>
    private const int MaxConcurrentRequests = 8;

    /// <summary>Token limit for the rate limiter.</summary>
    private const int RateLimitTokenLimit = 50;

    /// <summary>Tokens per period for the rate limiter.</summary>
    private const int RateLimitTokensPerPeriod = 10;

    /// <summary>Maximum retries for transient HTTP failures.</summary>
    private const int MaxRetryAttempts = 3;

    /// <summary>Default number of tokens to acquire from the rate limiter.</summary>
    private const int DefaultTokenAcquisitionCount = 1;

    /// <summary>Per-request HTTP timeout.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Replenishment period for the rate limiter.</summary>
    private static readonly TimeSpan RateLimitReplenishmentPeriod = TimeSpan.FromSeconds(1);

    /// <summary>Delay between retry attempts.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(0.5);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<int> ValidateAsync(SourceLinkEntry[] entries) =>
        ValidateAsync(entries, false, null);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<int> ValidateAsync(SourceLinkEntry[] entries, bool failOnBroken) =>
        ValidateAsync(entries, failOnBroken, null);

    /// <inheritdoc />
    public async Task<int> ValidateAsync(SourceLinkEntry[] entries, bool failOnBroken, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(entries);
        logger = GetLoggerOrDefault(logger);

        if (entries is [])
        {
            LogNothingToValidate(logger);
            return 0;
        }

        var byFileUrl = GroupByFileUrl(entries);
        LogValidating(logger, byFileUrl.Count, entries.Length);

        await using var rateLimiter = BuildRateLimiter();
        using var http = CreateHttpClient();
        var broken = await ValidateGroupedEntriesAsync(byFileUrl, rateLimiter, http).ConfigureAwait(false);

        ReportResults(byFileUrl.Count, broken, failOnBroken, logger);
        return broken.Count;
    }

    /// <summary>Gets the supplied logger or the null logger when none was provided.</summary>
    /// <param name="logger">The caller-supplied logger.</param>
    /// <returns>The logger to use.</returns>
    internal static ILogger GetLoggerOrDefault(ILogger? logger) => logger ?? NullLogger.Instance;

    /// <summary>Groups entries by file URL to deduplicate HEAD checks.</summary>
    /// <param name="entries">Entries to group.</param>
    /// <returns>A dictionary mapping file URLs to lists of symbol UIDs.</returns>
    internal static Dictionary<string, List<string>> GroupByFileUrl(SourceLinkEntry[] entries)
    {
        var byFileUrl = new Dictionary<string, List<string>>(entries.Length, StringComparer.Ordinal);

        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            var fileUrl = SourceUrlRewriter.StripAnchor(entry.Url);
            ref var uids = ref CollectionsMarshal.GetValueRefOrAddDefault(byFileUrl, fileUrl, out _);
            uids ??= [];
            uids.Add(entry.Uid);
        }

        return byFileUrl;
    }

    /// <summary>Creates the shared HTTP client used for HEAD checks.</summary>
    /// <returns>The configured HTTP client.</returns>
    internal static HttpClient CreateHttpClient() => new() { Timeout = RequestTimeout };

    /// <summary>Validates the grouped unique URLs and returns the broken-link set.</summary>
    /// <param name="byFileUrl">Grouped unique URLs and their referencing UIDs.</param>
    /// <param name="rateLimiter">Rate limiter used for each request attempt.</param>
    /// <param name="http">HTTP client used for HEAD checks.</param>
    /// <returns>The collected broken links.</returns>
    internal static async Task<ConcurrentBag<BrokenLink>> ValidateGroupedEntriesAsync(
        Dictionary<string, List<string>> byFileUrl,
        RateLimiter rateLimiter,
        HttpClient http)
    {
        var broken = new ConcurrentBag<BrokenLink>();
        var parallelOptions = CreateParallelOptions();

        await Parallel.ForEachAsync(
            byFileUrl,
            parallelOptions,
            async (entry, cancellationToken) =>
                await ValidateGroupedEntryAsync(entry, rateLimiter, http, broken, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

        return broken;
    }

    /// <summary>Creates the parallel options used for URL validation.</summary>
    /// <returns>The configured parallel options.</returns>
    internal static ParallelOptions CreateParallelOptions() => new() { MaxDegreeOfParallelism = MaxConcurrentRequests };

    /// <summary>Validates a single grouped URL and records any failure.</summary>
    /// <param name="entry">The grouped URL entry to validate.</param>
    /// <param name="rateLimiter">Rate limiter used for each request attempt.</param>
    /// <param name="http">HTTP client used for the HEAD check.</param>
    /// <param name="broken">Shared broken-link sink.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    internal static async Task ValidateGroupedEntryAsync(
        KeyValuePair<string, List<string>> entry,
        RateLimiter rateLimiter,
        HttpClient http,
        ConcurrentBag<BrokenLink> broken,
        CancellationToken cancellationToken)
    {
        var url = entry.Key;

        try
        {
            using var response = await SendHeadAsync(url, rateLimiter, http, cancellationToken).ConfigureAwait(false);
            if (response is { IsSuccessStatusCode: false })
            {
                broken.Add(CreateBrokenLink(url, entry.Value, $"HTTP {(int)response.StatusCode}"));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            broken.Add(CreateBrokenLink(url, entry.Value, ex.Message));
        }
    }

    /// <summary>Sends a rate-limited HEAD request, retrying transient HTTP failures.</summary>
    /// <param name="url">URL to validate.</param>
    /// <param name="rateLimiter">Rate limiter used for each request attempt.</param>
    /// <param name="http">HTTP client used for the HEAD check.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <param name="timeProvider">Clock used for retry delays; defaults to the system clock.</param>
    /// <returns>The HTTP response.</returns>
    /// <exception cref="InvalidOperationException">The rate limiter rejects the request.</exception>
    internal static async Task<HttpResponseMessage> SendHeadAsync(
        string url,
        RateLimiter rateLimiter,
        HttpClient http,
        CancellationToken cancellationToken,
        TimeProvider? timeProvider = null)
    {
        var delay = RetryDelay;
        for (var attempt = 0;; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var lease = await rateLimiter.AcquireAsync(DefaultTokenAcquisitionCount, cancellationToken).ConfigureAwait(false);
                if (!lease.IsAcquired)
                {
                    throw new InvalidOperationException("Source link request rejected by the rate limiter.");
                }

                using var request = CreateHeadRequest(url);
                return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < MaxRetryAttempts)
            {
                await Task.Delay(delay, timeProvider ?? TimeProvider.System, cancellationToken).ConfigureAwait(false);
                delay += delay;
            }
        }
    }

    /// <summary>Creates the HEAD request message for one URL.</summary>
    /// <param name="url">URL to validate.</param>
    /// <returns>The request message.</returns>
    internal static HttpRequestMessage CreateHeadRequest(string url) => new(HttpMethod.Head, new Uri(url));

    /// <summary>Creates a broken-link record from a grouped URL entry.</summary>
    /// <param name="url">Broken URL.</param>
    /// <param name="uids">UIDs that reference the URL.</param>
    /// <param name="reason">Reason the URL is considered broken.</param>
    /// <returns>The broken-link record.</returns>
    internal static BrokenLink CreateBrokenLink(string url, List<string> uids, string reason) => new(url, [.. uids], reason);

    /// <summary>Builds a token-bucket rate limiter.</summary>
    /// <returns>A new rate limiter.</returns>
    internal static TokenBucketRateLimiter BuildRateLimiter() => new(new()
    {
        TokenLimit = RateLimitTokenLimit,
        TokensPerPeriod = RateLimitTokensPerPeriod,
        ReplenishmentPeriod = RateLimitReplenishmentPeriod,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = int.MaxValue,
        AutoReplenishment = true,
    });

    /// <summary>Reports validation results.</summary>
    /// <param name="totalChecked">Total unique URLs checked.</param>
    /// <param name="broken">List of broken links.</param>
    /// <param name="failOnBroken">Whether to throw on failures.</param>
    /// <param name="logger">Logger for the report lines.</param>
    /// <exception cref="InvalidOperationException">Broken links were found and failure was requested.</exception>
    internal static void ReportResults(int totalChecked, ConcurrentBag<BrokenLink> broken, bool failOnBroken, ILogger logger)
    {
        if (broken.IsEmpty)
        {
            LogValidationPassed(logger, totalChecked);
            return;
        }

        LogValidationFoundBroken(logger, broken.Count, totalChecked);
        foreach (var entry in broken)
        {
            LogBrokenEntry(logger, entry.Reason, entry.Url, entry.Uids.Length);
        }

        if (!failOnBroken)
        {
            return;
        }

        throw new InvalidOperationException($"Source link validation failed: {broken.Count} links invalid.");
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

    /// <summary>Record for a broken link.</summary>
    /// <param name="Url">Broken URL.</param>
    /// <param name="Uids">Symbols referencing this URL.</param>
    /// <param name="Reason">Reason for failure.</param>
    internal sealed record BrokenLink(string Url, string[] Uids, string Reason);
}
