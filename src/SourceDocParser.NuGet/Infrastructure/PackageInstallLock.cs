// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Security.Cryptography;
using System.Text;

namespace SourceDocParser.NuGet;

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
    public static async Task<FileStream> AcquireAsync(string lockFilePath, TimeSpan? maxWait = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockFilePath);
        var dir = Path.GetDirectoryName(lockFilePath);
        if (TextHelpers.HasValue(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var deadline = DateTime.UtcNow + (maxWait ?? _defaultMaxWait);
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
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alreadyDone);
        ArgumentNullException.ThrowIfNull(work);
        var lockHandle = await AcquireAsync(lockFilePath, cancellationToken: cancellationToken).ConfigureAwait(false);
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
        byte[]? rented = null;
        var buffer = maxBytes <= 512
            ? stackalloc byte[512]
            : (rented = System.Buffers.ArrayPool<byte>.Shared.Rent(maxBytes));

        try
        {
            var written = Encoding.UTF8.GetBytes(packageInstallPath, buffer);
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(buffer[..written], hash);
            return string.Create(64, hash, static (dest, h) => Convert.TryToHexStringLower(h, dest, out _));
        }
        finally
        {
            if (rented != null)
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}
