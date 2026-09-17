// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>Retries HTTP request failures with bounded exponential backoff.</summary>
internal static class NuGetHttpRetry
{
    /// <summary>Limits each operation to seven total attempts.</summary>
    private const int RetryAttempts = 6;

    /// <summary>Initial retry delay in milliseconds.</summary>
    private const int InitialDelayMilliseconds = 200;

    /// <summary>Retries only HTTP failures; cancellation and other exceptions propagate immediately.</summary>
    /// <typeparam name="TResult">The operation result.</typeparam>
    /// <param name="operation">The complete HTTP operation to retry.</param>
    /// <param name="cancellationToken">Cancels requests and backoff delays.</param>
    /// <param name="delay">Optional delay implementation for deterministic timing.</param>
    /// <returns>The successful operation result.</returns>
    internal static async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        for (var attempt = 0;; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < RetryAttempts && !cancellationToken.IsCancellationRequested)
            {
                var duration = TimeSpan.FromMilliseconds(InitialDelayMilliseconds << attempt);
                await (delay is null ? Task.Delay(duration, cancellationToken) : delay(duration, cancellationToken)).ConfigureAwait(false);
            }
        }
    }
}
