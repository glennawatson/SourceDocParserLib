// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Security.Cryptography;
using System.Text;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>
/// Per-package file-system lock for the NuGet global cache —
/// ensures that two concurrent fetchers (or our fetcher running
/// alongside <c>dotnet restore</c> / Visual Studio) never try to
/// install the same <c>id</c> + <c>version</c> at once. Mirrors
/// the shape of NuGet's own <c>ConcurrencyUtilities</c>: take an
/// OS-level file lock keyed by a SHA256 of the package install
/// path, do the work under the lock, release on dispose.
/// </summary>
internal static class PackageInstallLock
{
    /// <summary>Sub-folder under the global packages root where lock files live (kept out of the per-package directory so deleting a half-extracted install doesn't strand the lock).</summary>
    private const string LocksFolderName = ".sdp-locks";

    /// <summary>Suffix added to the hashed lock file name.</summary>
    private const string LockFileSuffix = ".lock";

    /// <summary>UTF-8 stackalloc threshold before renting from the shared array pool.</summary>
    private const int StackAllocByteThreshold = 512;

    /// <summary>SHA-256 output size in bytes.</summary>
    private const int Sha256HashLength = 32;

    /// <summary>Lower-case hex character count for a SHA-256 hash.</summary>
    private const int Sha256HexLength = 64;

    /// <summary>How long to wait between lock retries.</summary>
    private static readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(50L);

    /// <summary>Default cap on lock-acquire wall time — surface a clear error rather than hang the build forever.</summary>
    private static readonly TimeSpan _defaultMaxWait = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Returns the absolute path to the lock file for
    /// <paramref name="packageInstallPath"/>. The name is the
    /// SHA256 hex of the install path so deeply-nested or
    /// non-ASCII directory names still produce a short,
    /// filesystem-safe lock filename.
    /// </summary>
    /// <param name="globalPackagesFolder">Root of the global cache (parent of the per-package directories).</param>
    /// <param name="packageInstallPath">Per-package install directory inside the global cache.</param>
    /// <returns>The absolute path to the lock file.</returns>
    public static string GetLockFilePath(string globalPackagesFolder, string packageInstallPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(globalPackagesFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageInstallPath);
        return Path.Combine(globalPackagesFolder, LocksFolderName, ComputeLockKey(packageInstallPath) + LockFileSuffix);
    }

    /// <summary>
    /// Acquires the per-package lock using the default wait limit and no cancellation token.
    /// </summary>
    /// <param name="lockFilePath">Path returned by <see cref="GetLockFilePath"/>.</param>
    /// <returns>An open <see cref="FileStream"/>; dispose to release the lock.</returns>
    public static Task<FileStream> AcquireAsync(string lockFilePath) =>
        AcquireAsync(lockFilePath, null, TimeProvider.System, CancellationToken.None);

    /// <summary>
    /// Acquires the per-package lock using the default wait limit.
    /// </summary>
    /// <param name="lockFilePath">Path returned by <see cref="GetLockFilePath"/>.</param>
    /// <param name="cancellationToken">Token observed across each retry.</param>
    /// <returns>An open <see cref="FileStream"/>; dispose to release the lock.</returns>
    public static Task<FileStream> AcquireAsync(string lockFilePath, in CancellationToken cancellationToken) =>
        AcquireAsync(lockFilePath, null, TimeProvider.System, cancellationToken);

    /// <summary>
    /// Acquires the per-package lock by opening a file with
    /// <see cref="FileShare.None"/> — other processes attempting
    /// the same <c>FileStream</c> open get an <see cref="IOException"/>
    /// and retry. Returns the open stream; caller disposes to
    /// release.
    /// </summary>
    /// <param name="lockFilePath">Path returned by <see cref="GetLockFilePath"/>.</param>
    /// <param name="maxWait">Optional cap on total wait time; defaults to 5 minutes.</param>
    /// <param name="cancellationToken">Token observed across each retry.</param>
    /// <returns>An open <see cref="FileStream"/>; dispose to release the lock.</returns>
    public static Task<FileStream> AcquireAsync(string lockFilePath, TimeSpan? maxWait, CancellationToken cancellationToken) =>
        AcquireAsync(lockFilePath, maxWait, TimeProvider.System, cancellationToken);

    /// <summary>
    /// Acquires the lock, re-checks the completion marker, and runs the work when needed.
    /// </summary>
    /// <param name="lockFilePath">Path returned by <see cref="GetLockFilePath"/>.</param>
    /// <param name="alreadyDone">Re-checks the install marker after the lock is acquired; returning true short-circuits the work.</param>
    /// <param name="work">Async install work to run under the lock.</param>
    /// <returns>True when <paramref name="work"/> ran; false when <paramref name="alreadyDone"/> short-circuited.</returns>
    public static Task<bool> RunUnderLockAsync(
        string lockFilePath,
        Func<bool> alreadyDone,
        Func<CancellationToken, Task> work) =>
        RunUnderLockAsync(lockFilePath, alreadyDone, work, CancellationToken.None);

    /// <summary>
    /// Convenience wrapper: acquires the lock, runs
    /// <paramref name="work"/>, releases. The work is invoked
    /// only if <paramref name="alreadyDone"/> returns false after
    /// the lock is acquired — pin the double-checked-lock
    /// pattern callers need so the install doesn't repeat when
    /// another process completed it while we waited.
    /// </summary>
    /// <param name="lockFilePath">Path returned by <see cref="GetLockFilePath"/>.</param>
    /// <param name="alreadyDone">Re-checks the install marker after the lock is acquired; returning true short-circuits the work.</param>
    /// <param name="work">Async install work to run under the lock.</param>
    /// <param name="cancellationToken">Token observed across the wait + the work.</param>
    /// <returns>True when <paramref name="work"/> ran; false when <paramref name="alreadyDone"/> short-circuited.</returns>
    public static async Task<bool> RunUnderLockAsync(
        string lockFilePath,
        Func<bool> alreadyDone,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alreadyDone);
        ArgumentNullException.ThrowIfNull(work);
        var lockHandle = await AcquireAsync(lockFilePath, (TimeSpan?)null, cancellationToken).ConfigureAwait(false);
        await using (lockHandle.ConfigureAwait(false))
        {
            if (alreadyDone())
            {
                return false;
            }

            await work(cancellationToken).ConfigureAwait(false);
            return true;
        }
    }

    /// <summary>
    /// Acquires the per-package lock using the supplied time provider for retry deadlines.
    /// </summary>
    /// <param name="lockFilePath">Path returned by <see cref="GetLockFilePath"/>.</param>
    /// <param name="maxWait">Optional cap on total wait time; defaults to 5 minutes.</param>
    /// <param name="timeProvider">Clock used to compute the retry deadline.</param>
    /// <param name="cancellationToken">Token observed across each retry.</param>
    /// <returns>An open <see cref="FileStream"/>; dispose to release the lock.</returns>
    private static async Task<FileStream> AcquireAsync(
        string lockFilePath,
        TimeSpan? maxWait,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockFilePath);
        ArgumentNullException.ThrowIfNull(timeProvider);
        var dir = Path.GetDirectoryName(lockFilePath);
        if (TextHelpers.HasValue(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var deadline = timeProvider.GetUtcNow() + (maxWait ?? _defaultMaxWait);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new(
                    lockFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose);
            }
            catch (IOException) when (timeProvider.GetUtcNow() < deadline)
            {
                await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Hashes <paramref name="packageInstallPath"/> to a fixed-length
    /// hex string used as the lock filename. Reuses NuGet's own
    /// "fold path to a hash" trick so the lock store stays flat and
    /// path-length safe regardless of how nested the install
    /// directory ends up.
    /// </summary>
    /// <param name="packageInstallPath">Path to hash.</param>
    /// <returns>Lower-case hex SHA-256 of the UTF-8 bytes.</returns>
    private static string ComputeLockKey(string packageInstallPath)
    {
        var maxBytes = Encoding.UTF8.GetMaxByteCount(packageInstallPath.Length);
        if (maxBytes <= StackAllocByteThreshold)
        {
            Span<byte> stackBuffer = stackalloc byte[StackAllocByteThreshold];
            return ComputeLockKey(packageInstallPath, stackBuffer);
        }

        var rented = System.Buffers.ArrayPool<byte>.Shared.Rent(maxBytes);
        try
        {
            return ComputeLockKey(packageInstallPath, rented);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Hashes the UTF-8 representation of the install path using the supplied scratch buffer.
    /// </summary>
    /// <param name="packageInstallPath">Path to hash.</param>
    /// <param name="buffer">Scratch buffer used for UTF-8 encoding.</param>
    /// <returns>Lower-case hex SHA-256 of the UTF-8 bytes.</returns>
    private static string ComputeLockKey(string packageInstallPath, Span<byte> buffer)
    {
        var written = Encoding.UTF8.GetBytes(packageInstallPath, buffer);
        Span<byte> hash = stackalloc byte[Sha256HashLength];
        SHA256.HashData(buffer[..written], hash);
        return string.Create(Sha256HexLength, hash, static (dest, h) => Convert.TryToHexStringLower(h, dest, out _));
    }
}
