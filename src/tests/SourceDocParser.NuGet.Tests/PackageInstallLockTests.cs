// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.NuGet.Infrastructure;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins <see cref="PackageInstallLock"/> against a real
/// filesystem -- concurrent acquisition serialises, the
/// double-checked-lock helper short-circuits when the install
/// marker shows up while we waited, and the lock file is keyed
/// by a stable hash of the install path.
/// </summary>
public class PackageInstallLockTests
{
    /// <summary>
    /// The lock file path is derived from a hash of the install
    /// path so the same package always lands on the same lock --
    /// concurrent waiters target the same file.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetLockFilePathIsStableForSameInstallPath()
    {
        var path1 = PackageInstallLock.GetLockFilePath("/cache", "/cache/splat/19.3.1");
        var path2 = PackageInstallLock.GetLockFilePath("/cache", "/cache/splat/19.3.1");

        await Assert.That(path1).IsEqualTo(path2);
    }

    /// <summary>
    /// Different install paths hash to different lock files --
    /// installs of unrelated packages don't serialise on each
    /// other.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetLockFilePathDiffersForDifferentInstallPaths()
    {
        var splat = PackageInstallLock.GetLockFilePath("/cache", "/cache/splat/19.3.1");
        var reactiveui = PackageInstallLock.GetLockFilePath("/cache", "/cache/reactiveui/23.2.1");

        await Assert.That(splat).IsNotEqualTo(reactiveui);
    }

    /// <summary>
    /// Acquire / release: the first call returns a held stream,
    /// the second call (in another task) blocks until the first
    /// disposes, then succeeds.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AcquireSerialisesConcurrentWaiters()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sdp-lock-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var lockPath = Path.Combine(dir, "test.lock");

        try
        {
            var first = await PackageInstallLock.AcquireAsync(lockPath, maxWait: TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false);

            var secondAcquired = false;
            var secondTask = Task.Run(async () =>
            {
                var second = await PackageInstallLock.AcquireAsync(lockPath, maxWait: TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
                await using (second.ConfigureAwait(false))
                {
                    secondAcquired = true;
                }
            });

            await Task.Delay(150).ConfigureAwait(false);
            await Assert.That(secondAcquired).IsFalse();

            await first.DisposeAsync().ConfigureAwait(false);
            await secondTask.ConfigureAwait(false);

            await Assert.That(secondAcquired).IsTrue();
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    /// <summary>
    /// RunUnderLockAsync runs the work when the install isn't
    /// already done, returning true.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RunUnderLockExecutesWorkWhenNotAlreadyDone()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sdp-lock-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var lockPath = Path.Combine(dir, "test.lock");

        try
        {
            var workRan = false;
            var ran = await PackageInstallLock.RunUnderLockAsync(
                lockPath,
                alreadyDone: () => false,
                work: ct =>
                {
                    workRan = true;
                    return Task.CompletedTask;
                }).ConfigureAwait(false);

            await Assert.That(ran).IsTrue();
            await Assert.That(workRan).IsTrue();
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Double-checked-lock: when alreadyDone returns true after
    /// the lock is acquired, the work is skipped.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RunUnderLockSkipsWorkWhenAlreadyDone()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sdp-lock-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var lockPath = Path.Combine(dir, "test.lock");

        try
        {
            var workRan = false;
            var ran = await PackageInstallLock.RunUnderLockAsync(
                lockPath,
                alreadyDone: () => true,
                work: ct =>
                {
                    workRan = true;
                    return Task.CompletedTask;
                }).ConfigureAwait(false);

            await Assert.That(ran).IsFalse();
            await Assert.That(workRan).IsFalse();
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    /// <summary>
    /// FileOptions.DeleteOnClose means the lock file vanishes when
    /// the holding stream disposes -- verify by acquiring, releasing,
    /// and asserting the file is gone.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task LockFileSelfDeletesAfterRelease()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sdp-lock-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var lockPath = Path.Combine(dir, "test.lock");

        try
        {
            var handle = await PackageInstallLock.AcquireAsync(lockPath).ConfigureAwait(false);
            await using (handle.ConfigureAwait(false))
            {
                await Assert.That(File.Exists(lockPath)).IsTrue();
            }

            await Assert.That(File.Exists(lockPath)).IsFalse();
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
