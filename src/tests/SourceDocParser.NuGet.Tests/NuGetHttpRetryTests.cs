// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.NuGet.Infrastructure;
using TUnit.Assertions.Enums;

namespace SourceDocParser.NuGet.Tests;

/// <summary>Verifies retry bounds, exponential delays, and cancellation without elapsed-time waits.</summary>
public class NuGetHttpRetryTests
{
    /// <summary>Result returned by a successful package operation.</summary>
    private const string PackageResult = "package";

    /// <summary>Delay before the first retry.</summary>
    private const int FirstRetryMilliseconds = 200;

    /// <summary>Delay before the second retry.</summary>
    private const int SecondRetryMilliseconds = 400;

    /// <summary>Delay before the third retry.</summary>
    private const int ThirdRetryMilliseconds = 800;

    /// <summary>Delay before the fourth retry.</summary>
    private const int FourthRetryMilliseconds = 1600;

    /// <summary>Delay before the fifth retry.</summary>
    private const int FifthRetryMilliseconds = 3200;

    /// <summary>Delay before the final retry.</summary>
    private const int FinalRetryMilliseconds = 6400;

    /// <summary>A successful request returns its result without scheduling a delay.</summary>
    /// <returns>The test operation.</returns>
    [Test]
    public async Task ExecuteAsyncReturnsFirstSuccessfulResult()
    {
        var attempts = 0;
        var delays = 0;
        var result = await NuGetHttpRetry.ExecuteAsync(
            _ =>
            {
                attempts++;
                return Task.FromResult(PackageResult);
            },
            CancellationToken.None,
            (_, _) =>
            {
                delays++;
                return Task.CompletedTask;
            });

        await Assert.That(result).IsEqualTo(PackageResult);
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(delays).IsEqualTo(0);
    }

    /// <summary>Transient HTTP failures retry with exponential backoff until the operation succeeds.</summary>
    /// <returns>The test operation.</returns>
    [Test]
    public async Task ExecuteAsyncRetriesHttpFailuresUntilSuccess()
    {
        const int successAttempt = 4;
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var result = await NuGetHttpRetry.ExecuteAsync(
            _ =>
            {
                attempts++;
                return attempts < successAttempt
                    ? Task.FromException<string>(new HttpRequestException("transient"))
                    : Task.FromResult(PackageResult);
            },
            CancellationToken.None,
            (duration, _) =>
            {
                delays.Add(duration);
                return Task.CompletedTask;
            });

        await Assert.That(result).IsEqualTo(PackageResult);
        await Assert.That(attempts).IsEqualTo(successAttempt);
        await Assert.That(delays).IsEquivalentTo(
            new[]
        {
            TimeSpan.FromMilliseconds(FirstRetryMilliseconds),
            TimeSpan.FromMilliseconds(SecondRetryMilliseconds),
            TimeSpan.FromMilliseconds(ThirdRetryMilliseconds),
        },
            CollectionOrdering.Matching);
    }

    /// <summary>The seventh failed attempt propagates its original exception without another delay.</summary>
    /// <returns>The test operation.</returns>
    [Test]
    public async Task ExecuteAsyncExhaustsSixRetries()
    {
        const int expectedAttempts = 7;
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var failure = new HttpRequestException("unavailable");

        var actual = await Assert.That(() => NuGetHttpRetry.ExecuteAsync<int>(
            _ =>
            {
                attempts++;
                throw failure;
            },
            CancellationToken.None,
            (duration, _) =>
            {
                delays.Add(duration);
                return Task.CompletedTask;
            })).Throws<HttpRequestException>();

        await Assert.That(actual).IsSameReferenceAs(failure);
        await Assert.That(attempts).IsEqualTo(expectedAttempts);
        await Assert.That(delays).IsEquivalentTo(
            new[]
        {
            TimeSpan.FromMilliseconds(FirstRetryMilliseconds),
            TimeSpan.FromMilliseconds(SecondRetryMilliseconds),
            TimeSpan.FromMilliseconds(ThirdRetryMilliseconds),
            TimeSpan.FromMilliseconds(FourthRetryMilliseconds),
            TimeSpan.FromMilliseconds(FifthRetryMilliseconds),
            TimeSpan.FromMilliseconds(FinalRetryMilliseconds),
        },
            CollectionOrdering.Matching);
    }

    /// <summary>Failures outside HTTP requests are propagated without a retry.</summary>
    /// <returns>The test operation.</returns>
    [Test]
    public async Task ExecuteAsyncDoesNotRetryOtherExceptions()
    {
        var attempts = 0;
        var delays = 0;

        await Assert.That(() => NuGetHttpRetry.ExecuteAsync<int>(
            _ =>
            {
                attempts++;
                throw new InvalidDataException("invalid package");
            },
            CancellationToken.None,
            (_, _) =>
            {
                delays++;
                return Task.CompletedTask;
            })).Throws<InvalidDataException>();

        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(delays).IsEqualTo(0);
    }

    /// <summary>A cancelled token prevents the first request from starting.</summary>
    /// <returns>The test operation.</returns>
    [Test]
    public async Task ExecuteAsyncRejectsCancellationBeforeRequest()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var attempts = 0;

        await Assert.That(() => NuGetHttpRetry.ExecuteAsync(
            _ =>
            {
                attempts++;
                return Task.FromResult(attempts);
            },
            cancellation.Token)).Throws<OperationCanceledException>();

        await Assert.That(attempts).IsEqualTo(0);
    }

    /// <summary>Cancellation inside a request propagates without scheduling retries.</summary>
    /// <returns>The test operation.</returns>
    [Test]
    public async Task ExecuteAsyncDoesNotRetryRequestCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var delays = 0;
        CancellationToken receivedToken = default;

        await Assert.That(() => NuGetHttpRetry.ExecuteAsync<int>(
            ct =>
            {
                attempts++;
                receivedToken = ct;
                throw new OperationCanceledException(ct);
            },
            cancellation.Token,
            (_, _) =>
            {
                delays++;
                return Task.CompletedTask;
            })).Throws<OperationCanceledException>();

        await Assert.That(receivedToken).IsEqualTo(cancellation.Token);
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(delays).IsEqualTo(0);
    }

    /// <summary>Cancellation during a backoff delay prevents another request.</summary>
    /// <returns>The test operation.</returns>
    [Test]
    public async Task ExecuteAsyncCancelsBackoff()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var delays = 0;
        CancellationToken receivedToken = default;

        await Assert.That(() => NuGetHttpRetry.ExecuteAsync<int>(
            _ =>
            {
                attempts++;
                throw new HttpRequestException("transient");
            },
            cancellation.Token,
            async (_, ct) =>
            {
                delays++;
                receivedToken = ct;
                await cancellation.CancelAsync();
                ct.ThrowIfCancellationRequested();
            })).Throws<OperationCanceledException>();

        await Assert.That(receivedToken).IsEqualTo(cancellation.Token);
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(delays).IsEqualTo(1);
    }
}
