// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace SourceDocParser.NuGet.Tests;

/// <summary>Constructs clients backed by per-test handlers without network connection pools.</summary>
internal static class TestHttpClientFactory
{
    /// <summary>Creates a client with the requested handler ownership.</summary>
    /// <param name="handler">The test's in-memory response handler.</param>
    /// <param name="disposeHandler">Whether disposing the client also disposes the handler.</param>
    /// <returns>A client scoped to the caller's test.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static HttpClient Create(HttpMessageHandler handler, bool disposeHandler = true) => new(handler, disposeHandler);
}
