// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.TestHelpers;

/// <summary>
/// Disposable scratch directory under the OS temp folder. Tests that
/// need a real filesystem path for the emitter / extractor instantiate
/// one inside a using block; the directory is recursively removed on
/// dispose.
/// </summary>
public sealed class ScratchDirectory : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScratchDirectory"/> class
    /// under the OS temp folder with a unique GUID-suffixed name.
    /// </summary>
    /// <param name="prefix">Optional prefix to make the directory easy to spot in logs.</param>
    public ScratchDirectory(string prefix = "sdp")
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    /// <summary>Gets the absolute path of the scratch directory.</summary>
    public string Path { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!Directory.Exists(Path))
        {
            return;
        }

        Directory.Delete(Path, recursive: true);
    }
}
