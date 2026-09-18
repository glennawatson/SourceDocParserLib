// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using NuGet.Common;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using NuGetLogLevel = NuGet.Common.LogLevel;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>Reports NuGet diagnostics through the documentation logger.</summary>
/// <param name="logger">Destination logger.</param>
internal sealed partial class PackageRestoreLogger(ILogger logger) : LoggerBase
{
    /// <inheritdoc />
    public override void Log(ILogMessage message)
    {
        var level = message.Level switch
        {
            NuGetLogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
            NuGetLogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
            _ => Microsoft.Extensions.Logging.LogLevel.Debug,
        };
        Write(logger, level, message.Code, message.Message);
    }

    /// <inheritdoc />
    public override Task LogAsync(ILogMessage message)
    {
        Log(message);
        return Task.CompletedTask;
    }

    /// <summary>Forwards a restore diagnostic.</summary>
    /// <param name="logger">Destination logger.</param>
    /// <param name="level">Diagnostic severity.</param>
    /// <param name="code">NuGet diagnostic code.</param>
    /// <param name="message">Diagnostic text.</param>
    [LoggerMessage(Message = "NuGet {Code}: {Message}")]
    private static partial void Write(ILogger logger, Microsoft.Extensions.Logging.LogLevel level, NuGetLogCode code, string message);
}
