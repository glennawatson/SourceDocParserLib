// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.RateLimiting;
using SourceDocParser.SourceLink;

namespace SourceDocParser.Tests;

/// <summary>Exercises HEAD request retries, throttling, cancellation, and broken-link reporting.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "PSH1418", Justification = "Each test needs its own in-memory handler and creates no network connections.")]
public sealed class SourceLinkValidatorTests
{
    /// <summary>Fixture value for TSample.</summary>
    private const string TSample = "T:Sample";

    /// <summary>Expected fixture value used by SendHeadAsyncRetriesTransientFailures.</summary>
    private const int SendHeadAsyncRetriesTransientFailuresValue = 3;

    /// <summary>Expected fixture value used by SendHeadAsyncRetriesTransientFailures.</summary>
    private const int SendHeadAsyncRetriesTransientFailuresExpectedValue = 4;

    /// <summary>Expected fixture value used by SendHeadAsyncRetriesTransientFailures.</summary>
    private const int SendHeadAsyncRetriesTransientFailuresFromMilliseconds = 500;

    /// <summary>Expected fixture value used by SendHeadAsyncRetriesTransientFailures.</summary>
    private const int SendHeadAsyncRetriesTransientFailuresFromSeconds = 2;

    /// <summary>URL used by the in-memory HTTP handler.</summary>
    private const string SourceUrl = "https://example.invalid/source.cs";

    /// <summary>Transient failures retry with exponential delays and release their leases before waiting.</summary>
    /// <param name="cancellationToken">Test cancellation token.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task SendHeadAsyncRetriesTransientFailures(CancellationToken cancellationToken)
    {
        await using var limiter = CreateLimiter();
        using var handler = new RecordingHandler(SendHeadAsyncRetriesTransientFailuresValue, HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        var clock = new ImmediateTimeProvider(limiter);

        using var response = await SourceLinkValidator.SendHeadAsync(SourceUrl, limiter, http, cancellationToken, clock);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(handler.Requests.Count).IsEqualTo(SendHeadAsyncRetriesTransientFailuresExpectedValue);
        await Assert.That(handler.Requests.TrueForAll(static request => request.Method == HttpMethod.Head)).IsTrue();
        await Assert.That(new HashSet<HttpRequestMessage>(handler.Requests).Count).IsEqualTo(SendHeadAsyncRetriesTransientFailuresExpectedValue);
        TimeSpan[] expectedDelays =
        [
            TimeSpan.FromMilliseconds(SendHeadAsyncRetriesTransientFailuresFromMilliseconds),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(SendHeadAsyncRetriesTransientFailuresFromSeconds),
        ];
        await Assert.That(clock.Delays).IsEquivalentTo(expectedDelays);
        await Assert.That(clock.AvailablePermits).IsEquivalentTo((long[])[1, 1, 1]);
        await Assert.That(limiter.GetStatistics()!.CurrentAvailablePermits).IsEqualTo(1);
    }

    /// <summary>The fourth transport failure escapes without scheduling another retry.</summary>
    /// <param name="cancellationToken">Test cancellation token.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task SendHeadAsyncStopsAfterThreeRetries(CancellationToken cancellationToken)
    {
        await using var limiter = CreateLimiter();
        using var handler = new RecordingHandler(SendHeadAsyncRetriesTransientFailuresExpectedValue, HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        var clock = new ImmediateTimeProvider(limiter);

        await Assert.That(async () =>
        {
            using var response = await SourceLinkValidator.SendHeadAsync(SourceUrl, limiter, http, cancellationToken, clock);
        }).Throws<HttpRequestException>();

        await Assert.That(handler.Requests.Count).IsEqualTo(SendHeadAsyncRetriesTransientFailuresExpectedValue);
        await Assert.That(clock.Delays.Count).IsEqualTo(SendHeadAsyncRetriesTransientFailuresValue);
        await Assert.That(limiter.GetStatistics()!.CurrentAvailablePermits).IsEqualTo(1);
    }

    /// <summary>HTTP error responses are returned without transport retries.</summary>
    /// <param name="statusCode">HTTP response status.</param>
    /// <param name="cancellationToken">Test cancellation token.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments(HttpStatusCode.NotFound)]
    [Arguments(HttpStatusCode.TooManyRequests)]
    [Arguments(HttpStatusCode.ServiceUnavailable)]
    public async Task SendHeadAsyncReturnsErrorStatusWithoutRetry(HttpStatusCode statusCode, CancellationToken cancellationToken)
    {
        await using var limiter = CreateLimiter();
        using var handler = new RecordingHandler(0, statusCode);
        using var http = new HttpClient(handler);
        var clock = new ImmediateTimeProvider(limiter);

        using var response = await SourceLinkValidator.SendHeadAsync(SourceUrl, limiter, http, cancellationToken, clock);

        await Assert.That(response.StatusCode).IsEqualTo(statusCode);
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
        await Assert.That(clock.Delays.Count).IsEqualTo(0);
    }

    /// <summary>A rejected lease prevents the HTTP request and is not retried.</summary>
    /// <param name="cancellationToken">Test cancellation token.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task SendHeadAsyncRejectsUnavailableLease(CancellationToken cancellationToken)
    {
        await using var limiter = CreateLimiter();
        using var heldLease = limiter.AttemptAcquire();
        using var handler = new RecordingHandler(0, HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        var clock = new ImmediateTimeProvider(limiter);

        await Assert.That(async () =>
        {
            using var response = await SourceLinkValidator.SendHeadAsync(SourceUrl, limiter, http, cancellationToken, clock);
        }).Throws<InvalidOperationException>();

        await Assert.That(handler.Requests.Count).IsEqualTo(0);
        await Assert.That(clock.Delays.Count).IsEqualTo(0);
    }

    /// <summary>Cancellation removes a queued rate-limit acquisition without sending a request.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2025", Justification = "The stored request is canceled and awaited before its limiter and client leave their using scopes.")]
    public async Task SendHeadAsyncCancelsQueuedLease()
    {
        await using var limiter = CreateLimiter(queueLimit: 1);
        using var heldLease = limiter.AttemptAcquire();
        using var handler = new RecordingHandler(0, HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();

        var pending = SourceLinkValidator.SendHeadAsync(SourceUrl, limiter, http, cancellation.Token);
        await Assert.That(limiter.GetStatistics()!.CurrentQueuedCount).IsEqualTo(1);
        await cancellation.CancelAsync();

        OperationCanceledException? cancellationError = null;
        try
        {
            using var response = await pending;
        }
        catch (OperationCanceledException exception)
        {
            cancellationError = exception;
        }

        await Assert.That(cancellationError).IsNotNull();
        await Assert.That(handler.Requests.Count).IsEqualTo(0);
        await Assert.That(limiter.GetStatistics()!.CurrentQueuedCount).IsEqualTo(0);
    }

    /// <summary>Cancellation during backoff prevents any further request attempt.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task SendHeadAsyncCancelsRetryDelay()
    {
        await using var limiter = CreateLimiter();
        using var handler = new RecordingHandler(1, HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var clock = new ImmediateTimeProvider(limiter, cancellation.Cancel);

        await Assert.That(async () =>
        {
            using var response = await SourceLinkValidator.SendHeadAsync(SourceUrl, limiter, http, cancellation.Token, clock);
        }).Throws<OperationCanceledException>();

        await Assert.That(handler.Requests.Count).IsEqualTo(1);
        await Assert.That(limiter.GetStatistics()!.CurrentAvailablePermits).IsEqualTo(1);
    }

    /// <summary>Unsuccessful HEAD responses become broken-link records and their response content is disposed.</summary>
    /// <param name="cancellationToken">Test cancellation token.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task ValidateGroupedEntryAsyncRecordsStatusAndDisposesResponse(CancellationToken cancellationToken)
    {
        await using var limiter = CreateLimiter();
        using var handler = new RecordingHandler(0, HttpStatusCode.NotFound);
        using var http = new HttpClient(handler);
        var broken = new ConcurrentBag<SourceLinkValidator.BrokenLink>();

        await SourceLinkValidator.ValidateGroupedEntryAsync(new(SourceUrl, [TSample]), limiter, http, broken, cancellationToken);

        var entries = broken.ToArray();
        await Assert.That(entries.Length).IsEqualTo(1);
        var entry = entries[0];
        await Assert.That(entry.Url).IsEqualTo(SourceUrl);
        await Assert.That(entry.Reason).IsEqualTo("HTTP 404");
        await Assert.That(entry.Uids).IsEquivalentTo((string[])[TSample]);
        await Assert.That(async () =>
        {
            _ = await handler.Response!.Content.ReadAsStringAsync(cancellationToken);
        }).Throws<ObjectDisposedException>();
    }

    /// <summary>Caller cancellation escapes validation instead of being reported as a broken URL.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task ValidateGroupedEntryAsyncPropagatesCancellation()
    {
        await using var limiter = CreateLimiter();
        using var handler = new RecordingHandler(0, HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var broken = new ConcurrentBag<SourceLinkValidator.BrokenLink>();
        await cancellation.CancelAsync();

        await Assert.That(() => SourceLinkValidator.ValidateGroupedEntryAsync(new(SourceUrl, [TSample]), limiter, http, broken, cancellation.Token)).Throws<OperationCanceledException>();

        await Assert.That(broken.IsEmpty).IsTrue();
        await Assert.That(handler.Requests.Count).IsEqualTo(0);
    }

    /// <summary>Creates a single-permit limiter for deterministic lease checks.</summary>
    /// <param name="queueLimit">Number of requests allowed to await a permit.</param>
    /// <returns>The configured limiter.</returns>
    private static ConcurrencyLimiter CreateLimiter(int queueLimit = 0) => new(new() { PermitLimit = 1, QueueLimit = queueLimit, QueueProcessingOrder = QueueProcessingOrder.OldestFirst, });

    /// <summary>Returns transport failures followed by a configured HTTP status.</summary>
    /// <param name="failureCount">Number of transport failures before success.</param>
    /// <param name="statusCode">Response status after the failures.</param>
    private sealed class RecordingHandler(int failureCount, HttpStatusCode statusCode) : HttpMessageHandler
    {
        /// <summary>Gets the request instances received by the handler.</summary>
        public List<HttpRequestMessage> Requests { get; } = [];

        /// <summary>Gets the most recent response.</summary>
        public HttpResponseMessage? Response { get; private set; }

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Requests.Count <= failureCount)
            {
                return Task.FromException<HttpResponseMessage>(new HttpRequestException("Transport unavailable."));
            }

            Response = new(statusCode) { Content = new StringContent("response") };
            return Task.FromResult(Response);
        }
    }

    /// <summary>Completes delay timers immediately while recording durations and outstanding leases.</summary>
    /// <param name="limiter">Limiter whose permits are sampled when a delay starts.</param>
    /// <param name="onDelay">Optional callback before completing a delay.</param>
    private sealed class ImmediateTimeProvider(ConcurrencyLimiter limiter, Action? onDelay = null) : TimeProvider
    {
        /// <summary>Gets the requested retry durations.</summary>
        public List<TimeSpan> Delays { get; } = [];

        /// <summary>Gets the available permits at the beginning of each delay.</summary>
        public List<long> AvailablePermits { get; } = [];

        /// <inheritdoc/>
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Delays.Add(dueTime);
            AvailablePermits.Add(limiter.GetStatistics()!.CurrentAvailablePermits);
            onDelay?.Invoke();
            callback(state);
            return new CompletedTimer();
        }
    }

    /// <summary>Represents an immediately completed timer without scheduling wall-clock work.</summary>
    private sealed class CompletedTimer : ITimer
    {
        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;

        /// <inheritdoc/>
        public void Dispose()
        {
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
