// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

// Integration tests fetch packages, extract assemblies, walk symbol
// trees, and emit pages -- all heavy disk + network I/O. Run the
// whole assembly serially so concurrent tests don't fan out the
// /tmp scratch usage or fight over the shared global package cache.
[assembly: NotInParallel]
