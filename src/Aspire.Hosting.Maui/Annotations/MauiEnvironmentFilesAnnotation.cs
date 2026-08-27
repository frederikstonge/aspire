// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting.Maui.Annotations;

/// <summary>
/// Carries a lazily-evaluated, cached generator for the MSBuild inputs that surface a resource's
/// <c>WithEnvironment</c> values to a MAUI build.
/// </summary>
/// <remarks>
/// The inputs can only be produced at launch time because environment values are resolved then. Each resource
/// exposes its environment variables as global MSBuild properties (a set of <c>-p:NAME=VALUE</c> arguments);
/// Android and iOS additionally emit a targets file carrying the platform launch items. For Android and iOS
/// the exact same inputs are needed by two consumers: the serialized pre-build in
/// <c>MauiBuildQueueEventSubscriber</c> and the later DCP <c>/t:Run</c> launch command (via each platform's
/// command-line callback). For Windows and Mac Catalyst there is no launch-command callback — only the
/// pre-build consumes them. Generation is therefore deferred and cached here so that, when there are multiple
/// consumers, whichever runs first produces the inputs and the rest reuse them — independent of event
/// subscriber ordering.
/// <para>
/// The cache is scoped to a single launch, not to the whole app-model lifetime. Environment values can
/// change between a stop and a restart, so <see cref="Invalidate"/> is called at the start of each
/// <c>BeforeResourceStartedEvent</c> to drop inputs generated for a previous start; the current launch then
/// regenerates them once and its consumers reuse the fresh inputs.
/// </para>
/// </remarks>
internal sealed class MauiEnvironmentFilesAnnotation(
    Func<ILogger, CancellationToken, Task<(IReadOnlyList<string> PropertyArgs, string? TargetsFilePath)>> generateAsync) : IResourceAnnotation
{
    private readonly Func<ILogger, CancellationToken, Task<(IReadOnlyList<string> PropertyArgs, string? TargetsFilePath)>> _generateAsync = generateAsync;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (IReadOnlyList<string> PropertyArgs, string? TargetsFilePath)? _generated;

    /// <summary>
    /// Generates the environment inputs on the first call and returns the cached values on subsequent calls
    /// until <see cref="Invalidate"/> is called.
    /// </summary>
    public async Task<(IReadOnlyList<string> PropertyArgs, string? TargetsFilePath)> GetOrCreateAsync(ILogger logger, CancellationToken cancellationToken)
    {
        if (_generated is { } generated)
        {
            return generated;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _generated ??= await _generateAsync(logger, cancellationToken).ConfigureAwait(false);
            return _generated.Value;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Drops the cached inputs so the next <see cref="GetOrCreateAsync"/> regenerates them. Called at the
    /// start of each launch so a restart does not reuse inputs built from stale environment values.
    /// </summary>
    public void Invalidate()
    {
        _gate.Wait();
        try
        {
            _generated = null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
