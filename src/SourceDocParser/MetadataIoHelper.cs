// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

/// <summary>
/// Helper methods for Metadata Extractor I/O operations.
/// </summary>
internal static class MetadataIoHelper
{
    /// <summary>
    /// Prepares the output directory by deleting it if it exists and creating it.
    /// </summary>
    /// <param name="outputRoot">The output root path.</param>
    public static void PrepareOutputDirectory(string outputRoot)
    {
        if (Directory.Exists(outputRoot))
        {
            Directory.Delete(outputRoot, recursive: true);
        }

        Directory.CreateDirectory(outputRoot);
    }
}
