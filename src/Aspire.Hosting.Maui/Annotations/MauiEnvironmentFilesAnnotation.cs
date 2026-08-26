// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting.Maui.Annotations;

/// <summary>
/// Carries a lazily-evaluated, cached generator for the MSBuild files that surface a resource's
/// <c>WithEnvironment</c> values to a MAUI build.
/// </summary>
/// <remarks>
/// The files can only be generated at launch time because environment values are resolved then. For Android
/// and iOS the exact same file paths are needed by two consumers: the serialized pre-build in
/// <c>MauiBuildQueueEventSubscriber</c> and the later DCP <c>/t:Run</c> launch command (via each platform's
/// command-line callback). For Windows and Mac Catalyst there is no launch-command callback — only the
/// pre-build consumes the props file. Generation is therefore deferred and cached here so that, when there
/// are multiple consumers, whichever runs first produces the files and the rest reuse them — independent of
/// event subscriber ordering.
/// </remarks>
internal sealed class MauiEnvironmentFilesAnnotation(
    Func<ILogger, CancellationToken, Task<(string? PropsFilePath, string? TargetsFilePath)>> generateAsync) : IResourceAnnotation
{
    private readonly Func<ILogger, CancellationToken, Task<(string? PropsFilePath, string? TargetsFilePath)>> _generateAsync = generateAsync;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (string? PropsFilePath, string? TargetsFilePath)? _generated;

    /// <summary>
    /// Generates the environment files on the first call and returns the cached paths on subsequent calls.
    /// </summary>
    public async Task<(string? PropsFilePath, string? TargetsFilePath)> GetOrCreateAsync(ILogger logger, CancellationToken cancellationToken)
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
}
