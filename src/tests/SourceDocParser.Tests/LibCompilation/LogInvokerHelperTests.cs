// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDocParser.LibCompilation;

namespace SourceDocParser.Tests.LibCompilation;

/// <summary>
/// Pins the invocation-gating contract of <see cref="LogInvokerHelper"/>:
/// the action only runs when the logger is enabled, and the projector
/// arg of the 3-state overload runs only on the enabled path.
/// </summary>
public class LogInvokerHelperTests
{
    /// <summary>Expected fixture value used by ProjectingOverloadRunsProjectorAndActionWhenEnabled.</summary>
    private const int ProjectingOverloadRunsProjectorAndActionWhenEnabledInvoke = 2;

    /// <summary>Expected fixture value used by ProjectingOverloadRunsProjectorAndActionWhenEnabled.</summary>
    private const int ProjectingOverloadRunsProjectorAndActionWhenEnabledExpectedValue = 6;

    /// <summary>The 2-state overload runs the action when the logger is enabled.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TwoStateOverloadInvokesActionWhenEnabled()
    {
        var logger = new TogglingLogger(enabled: true);
        var calls = 0;
        var capturedA = 0;
        string? capturedB = null;

        LogInvokerHelper.Invoke(logger, LogLevel.Information, 1, "two", (_, a, b) =>
        {
            calls++;
            capturedA = a;
            capturedB = b;
        });

        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(capturedA).IsEqualTo(1);
        await Assert.That(capturedB).IsEqualTo("two");
    }

    /// <summary>The 2-state overload skips the action when the logger is disabled.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TwoStateOverloadSkipsActionWhenDisabled()
    {
        var logger = new TogglingLogger(enabled: false);
        var calls = 0;

        LogInvokerHelper.Invoke(logger, LogLevel.Information, 1, "two", (_, _, _) => calls++);

        await Assert.That(calls).IsEqualTo(0);
    }

    /// <summary>The 3-state overload runs the projector + action when enabled.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProjectingOverloadRunsProjectorAndActionWhenEnabled()
    {
        var logger = new TogglingLogger(enabled: true);
        var projections = 0;
        var calls = 0;
        var capturedProjected = 0;

        LogInvokerHelper.Invoke(
            logger,
            LogLevel.Information,
            1,
            "two",
            arg3: 3,
            projector: x =>
            {
                projections++;
                return x * ProjectingOverloadRunsProjectorAndActionWhenEnabledInvoke;
            },
            action: (_, _, _, projected) =>
            {
                calls++;
                capturedProjected = projected;
            });

        await Assert.That(projections).IsEqualTo(1);
        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(capturedProjected).IsEqualTo(ProjectingOverloadRunsProjectorAndActionWhenEnabledExpectedValue);
    }

    /// <summary>The 3-state overload skips both projector and action when disabled.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ProjectingOverloadSkipsBothWhenDisabled()
    {
        var logger = new TogglingLogger(enabled: false);
        var projections = 0;
        var calls = 0;

        LogInvokerHelper.Invoke(
            logger,
            LogLevel.Information,
            1,
            "two",
            arg3: 3,
            projector: x =>
            {
                projections++;
                return x;
            },
            action: (_, _, _, _) => calls++);

        await Assert.That(projections).IsEqualTo(0);
        await Assert.That(calls).IsEqualTo(0);
    }

    /// <summary>Both overloads reject null arguments.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RejectsNullArguments()
    {
        await Assert.That(static () => LogInvokerHelper.Invoke(null!, LogLevel.Information, 0, 0, static (_, _, _) => { })).Throws<ArgumentNullException>();
        await Assert.That(static () => LogInvokerHelper.Invoke(NullLogger.Instance, LogLevel.Information, 0, 0, null!)).Throws<ArgumentNullException>();
        await Assert.That(static () => LogInvokerHelper.Invoke<int, int, int, int>(
            NullLogger.Instance,
            LogLevel.Information,
            0,
            0,
            0,
            projector: null!,
            static (_, _, _, _) => { })).Throws<ArgumentNullException>();
        await Assert.That(static () => LogInvokerHelper.Invoke(NullLogger.Instance, LogLevel.Information, 0, 0, 0, projector: static x => x, action: null!)).Throws<ArgumentNullException>();
    }

    /// <summary>Test helper -- minimal ILogger whose <c>IsEnabled</c> returns a fixed value.</summary>
    private sealed class TogglingLogger : ILogger
    {
        /// <summary>Configured enabled-flag.</summary>
        private readonly bool _enabled;

        /// <summary>Initializes a new instance of the <see cref="TogglingLogger"/> class.</summary>
        /// <param name="enabled">Whether the logger should report enabled.</param>
        public TogglingLogger(bool enabled) => _enabled = enabled;

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsEnabled(LogLevel logLevel) => _enabled;

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}
