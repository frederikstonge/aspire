// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma warning disable ASPIREFILESYSTEM001 // Type is for evaluation purposes only

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Aspire.Hosting.Lifecycle;
using Aspire.Hosting.Maui.Utilities;
using Aspire.Hosting.Tests.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Aspire.Hosting.Tests;

/// <summary>
/// Regression tests for the MAUI environment subscribers. Unlike <see cref="MauiEnvironmentHelperTests"/>,
/// which only inspects the helper-generated XML, these tests drive the subscriber's command-line callback so
/// that a regression which stops importing the generated files (for example dropping the
/// <c>CustomBeforeMicrosoftCommonProps</c> argument) is caught.
/// </summary>
public class MauiEnvironmentSubscriberTests(ITestOutputHelper outputHelper)
{
    [Fact]
    public async Task AndroidSubscriber_ImportsGeneratedPropsAndTargets()
    {
        using var workspace = TemporaryWorkspace.Create(outputHelper);
        var tempFile = Path.Combine(workspace.Path, "TempMauiProject.csproj");
        File.WriteAllText(tempFile, MauiTestHelper.CreateProjectContent("net10.0-android"));

        var appBuilder = DistributedApplication.CreateBuilder();
        var maui = appBuilder.AddMauiProject("mauiapp", tempFile);
        var android = maui.AddAndroidEmulator()
            .WithEnvironment("MY_VAR", "hello");

        await using var app = appBuilder.Build();

        var subscriber = new MauiAndroidEnvironmentSubscriber(
            app.Services.GetRequiredService<DistributedApplicationExecutionContext>(),
            app.Services.GetRequiredService<ResourceLoggerService>(),
            app.Services.GetRequiredService<ResourceNotificationService>(),
            app.Services.GetRequiredService<IFileSystemService>());

        await PublishBeforeResourceStartedAsync(app, subscriber, android.Resource);

        var args = await ArgumentEvaluator.GetArgumentListAsync(android.Resource);

        // The props file must be imported early (before the project body) and the targets file late; both
        // are required for the injected environment values to reach the build and the launch tooling.
        Assert.Contains(args, a => a.StartsWith("-p:CustomBeforeMicrosoftCommonProps=", StringComparison.Ordinal));
        Assert.Contains(args, a => a.StartsWith("-p:CustomAfterMicrosoftCommonTargets=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task iOSSubscriber_ImportsGeneratedPropsAndTargets()
    {
        using var workspace = TemporaryWorkspace.Create(outputHelper);
        var tempFile = Path.Combine(workspace.Path, "TempMauiProject.csproj");
        File.WriteAllText(tempFile, MauiTestHelper.CreateProjectContent("net10.0-ios"));

        var appBuilder = DistributedApplication.CreateBuilder();
        var maui = appBuilder.AddMauiProject("mauiapp", tempFile);
        var ios = maui.AddiOSSimulator()
            .WithEnvironment("MY_VAR", "hello");

        await using var app = appBuilder.Build();

        var subscriber = new MauiiOSEnvironmentSubscriber(
            app.Services.GetRequiredService<DistributedApplicationExecutionContext>(),
            app.Services.GetRequiredService<ResourceLoggerService>(),
            app.Services.GetRequiredService<ResourceNotificationService>(),
            app.Services.GetRequiredService<IFileSystemService>());

        await PublishBeforeResourceStartedAsync(app, subscriber, ios.Resource);

        var args = await ArgumentEvaluator.GetArgumentListAsync(ios.Resource);

        Assert.Contains(args, a => a.StartsWith("-p:CustomBeforeMicrosoftCommonProps=", StringComparison.Ordinal));
        Assert.Contains(args, a => a.StartsWith("-p:CustomAfterMicrosoftCommonTargets=", StringComparison.Ordinal));
    }

    private static async Task PublishBeforeResourceStartedAsync(
        DistributedApplication app,
        IDistributedApplicationEventingSubscriber subscriber,
        IResource resource)
    {
        var eventing = app.Services.GetRequiredService<IDistributedApplicationEventing>();
        var execContext = app.Services.GetRequiredService<DistributedApplicationExecutionContext>();

        await subscriber.SubscribeAsync(eventing, execContext, CancellationToken.None);
        await eventing.PublishAsync(new BeforeResourceStartedEvent(resource, app.Services), CancellationToken.None);
    }
}
