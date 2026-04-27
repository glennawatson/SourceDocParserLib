// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SamplePdb;

/// <summary>Async method — produces a compiler-generated state machine display class.</summary>
public class Asynchronous
{
    /// <summary>Awaits a no-op task.</summary>
    /// <returns>A task that completes immediately.</returns>
    public async Task RunAsync() => await Task.Yield();
}