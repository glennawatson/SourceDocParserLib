// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Globalization;

namespace SourceDocParser.Zensical.Tests;

/// <summary>Self-cleaning temporary directory used by file-emitting tests.</summary>
internal sealed class TempDirectory : IDisposable
{
    /// <summary>Initializes a new instance of the <see cref="TempDirectory"/> class.</summary>
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sdp-zensical-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(Path);
    }

    /// <summary>Gets the absolute path of the temporary directory.</summary>
    public string Path { get; }

    /// <summary>Removes the directory and everything under it.</summary>
    public void Dispose()
    {
        if (!Directory.Exists(Path))
        {
            return;
        }

        Directory.Delete(Path, recursive: true);
    }
}
