// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.Extensions.Logging.Abstractions;
using SourceDocParser.LibCompilation;

namespace SourceDocParser.Tests.LibCompilation;

/// <summary>
/// Pins the parse-failure fallback of <see cref="XmlDocsLoader"/> --
/// the success and missing-file paths are exercised via the wider
/// metadata-cache integration tests, but the catch arm only fires
/// when the .xml entry is unreadable as a file (e.g. it's a
/// directory, or read perm denied).
/// </summary>
public class XmlDocsLoaderTests
{
    /// <summary>A missing .xml sibling returns null without logging an error.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryLoadReturnsNullWhenXmlSiblingMissing()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sdp-xml-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            var assemblyPath = Path.Combine(dir, "fake.dll");
            await File.WriteAllBytesAsync(assemblyPath, [0x4D, 0x5A]);

            var docs = XmlDocsLoader.TryLoad(assemblyPath, NullLogger.Instance);

            await Assert.That(docs).IsNull();
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
    /// Validates the fallback behavior of <see cref="XmlDocsLoader.TryLoad"/> when the associated .xml file is unreadable (e.g., no read permissions or it is a directory).
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryLoadReturnsNullWhenXmlIsUnreadable()
    {
        if (IsRunningAsRootOrWindows())
        {
            return;
        }

        var dir = Path.Combine(Path.GetTempPath(), $"sdp-xml-bad-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            var assemblyPath = Path.Combine(dir, "fake.dll");
            var xmlPath = Path.Combine(dir, "fake.xml");
            await File.WriteAllBytesAsync(assemblyPath, [0x4D, 0x5A]);
            await File.WriteAllTextAsync(xmlPath, "<doc/>");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(xmlPath, UnixFileMode.None);
            }

            var docs = XmlDocsLoader.TryLoad(assemblyPath, NullLogger.Instance);

            await Assert.That(docs).IsNull();
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                var xmlPath = Path.Combine(dir, "fake.xml");
                if (File.Exists(xmlPath) && !OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(xmlPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }

                Directory.Delete(dir, recursive: true);
            }
        }
    }

    /// <summary>A valid .xml sibling produces a non-null documentation provider.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryLoadReturnsProviderForValidXml()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sdp-xml-good-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            var assemblyPath = Path.Combine(dir, "fake.dll");
            var xmlPath = Path.Combine(dir, "fake.xml");
            await File.WriteAllBytesAsync(assemblyPath, [0x4D, 0x5A]);
            const string xml = """
                <doc>
                  <members>
                    <member name="T:Foo">
                      <summary>S</summary>
                    </member>
                  </members>
                </doc>
                """;
            await File.WriteAllTextAsync(xmlPath, xml);

            var docs = XmlDocsLoader.TryLoad(assemblyPath, NullLogger.Instance);

            await Assert.That(docs).IsNotNull();
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    /// <summary>Returns true on Windows or when the process is running as root (UID 0).</summary>
    /// <returns>True when this environment can't enforce chmod 000.</returns>
    private static bool IsRunningAsRootOrWindows() =>
        OperatingSystem.IsWindows()
        || (Environment.GetEnvironmentVariable("USER") is "root")
        || (Environment.GetEnvironmentVariable("UID") is "0");
}
