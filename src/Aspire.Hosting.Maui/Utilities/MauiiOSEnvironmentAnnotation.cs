// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Aspire.Hosting.Lifecycle;
using Aspire.Hosting.Maui.Annotations;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting.Maui.Utilities;

/// <summary>
/// Annotation that enables iOS environment variable support via MSBuild targets file.
/// </summary>
/// <remarks>
/// iOS MAUI applications cannot receive environment variables directly through the process environment
/// when launched via `dotnet run`. Instead, environment variables must be passed through MSBuild properties.
/// This annotation marks a resource for processing by <see cref="MauiiOSEnvironmentSubscriber"/>.
/// </remarks>
internal sealed class MauiiOSEnvironmentAnnotation : IResourceAnnotation
{
    // Marker annotation - actual logic is in the eventing subscriber
}

/// <summary>
/// Internal annotation to track that the callback for iOS environment variables has been registered.
/// </summary>
/// <remarks>
/// This is a marker annotation used to prevent duplicate callback registration.
/// The actual file path is managed within the callback closure and doesn't need to be stored here.
/// </remarks>
internal sealed class MauiiOSEnvironmentProcessedAnnotation : IResourceAnnotation
{
}

/// <summary>
/// Event subscriber that processes <see cref="MauiiOSEnvironmentAnnotation"/> annotations.
/// </summary>
internal sealed class MauiiOSEnvironmentSubscriber(
    ResourceLoggerService loggerService,
    ResourceNotificationService notificationService) : IDistributedApplicationEventingSubscriber
{
    public Task SubscribeAsync(IDistributedApplicationEventing eventing, DistributedApplicationExecutionContext execContext, CancellationToken cancellationToken)
    {
        eventing.Subscribe<BeforeResourceStartedEvent>(OnBeforeResourceStartedAsync);
        return Task.CompletedTask;
    }

    private async Task OnBeforeResourceStartedAsync(BeforeResourceStartedEvent @event, CancellationToken cancellationToken)
    {
        var resource = @event.Resource;

        // Only process iOS resources with the environment annotation
        if (resource is not (MauiiOSDeviceResource or MauiiOSSimulatorResource))
        {
            return;
        }

        if (!resource.TryGetLastAnnotation<MauiiOSEnvironmentAnnotation>(out _))
        {
            return;
        }

        var logger = loggerService.GetLogger(resource);

        // Check if we've already added the callback
        if (resource.TryGetLastAnnotation<MauiiOSEnvironmentProcessedAnnotation>(out _))
        {
            // Already processed - callback is already registered
            return;
        }

        try
        {
            // Add a CommandLineArgsCallback that appends the environment MSBuild inputs to the DCP launch
            // command. The inputs themselves are produced by the MauiEnvironmentFilesAnnotation attached at
            // resource creation, which caches them so the serialized pre-build and this launch share the
            // exact same values regardless of subscriber ordering.
            resource.Annotations.Add(new CommandLineArgsCallbackAnnotation(async context =>
            {
                if (!resource.TryGetLastAnnotation<MauiEnvironmentFilesAnnotation>(out var envFiles))
                {
                    return;
                }

                var (propertyArgs, targetsFilePath) = await envFiles.GetOrCreateAsync(context.Logger, context.CancellationToken).ConfigureAwait(false);

                // Passed as global MSBuild properties (rather than importing a props file via the single
                // CustomBeforeMicrosoftCommonProps slot) so the user's own props extension is preserved.
                // Global properties are visible to project-level property definitions and conditions.
                foreach (var propertyArg in propertyArgs)
                {
                    context.Args.Add(propertyArg);
                }

                // The targets file is imported late so the mlaunch launch item hooks run after the common
                // targets have defined them.
                if (targetsFilePath is not null)
                {
                    context.Args.Add($"-p:CustomAfterMicrosoftCommonTargets={targetsFilePath}");
                }
            }));

            // Mark as processed to avoid duplicate callbacks
            resource.Annotations.Add(new MauiiOSEnvironmentProcessedAnnotation());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to configure iOS environment variables");

            // Report the error through the notification service
            await notificationService.PublishUpdateAsync(resource, s => s with
            {
                State = new ResourceStateSnapshot("Failed to configure environment", KnownResourceStateStyles.Error)
            }).ConfigureAwait(false);

            throw;
        }
    }
}
