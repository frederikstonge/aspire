// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Aspire.Hosting.Lifecycle;
using Aspire.Hosting.Maui.Annotations;
using Aspire.Hosting.Maui.Lifecycle;
using Aspire.Hosting.Maui.Utilities;
using Aspire.Hosting.Tests.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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
            app.Services.GetRequiredService<ResourceLoggerService>(),
            app.Services.GetRequiredService<ResourceNotificationService>());

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
            app.Services.GetRequiredService<ResourceLoggerService>(),
            app.Services.GetRequiredService<ResourceNotificationService>());

        await PublishBeforeResourceStartedAsync(app, subscriber, ios.Resource);

        var args = await ArgumentEvaluator.GetArgumentListAsync(ios.Resource);

        Assert.Contains(args, a => a.StartsWith("-p:CustomBeforeMicrosoftCommonProps=", StringComparison.Ordinal));
        Assert.Contains(args, a => a.StartsWith("-p:CustomAfterMicrosoftCommonTargets=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AndroidBuild_ImportsGeneratedPropsAndTargets()
    {
        using var workspace = TemporaryWorkspace.Create(outputHelper);
        var tempFile = Path.Combine(workspace.Path, "TempMauiProject.csproj");
        File.WriteAllText(tempFile, MauiTestHelper.CreateProjectContent("net10.0-android"));

        var appBuilder = DistributedApplication.CreateBuilder();
        var maui = appBuilder.AddMauiProject("mauiapp", tempFile);
        var android = maui.AddAndroidEmulator()
            .WithEnvironment("MY_VAR", "hello");

        await using var app = appBuilder.Build();

        var args = await GetBuildArgumentsAsync(android.Resource);

        // The actual `dotnet build` (not just the launch command) must import the generated files, otherwise
        // build-time conditions and Android environment items never see WithEnvironment values.
        Assert.Contains(args, a => a.StartsWith("-p:CustomBeforeMicrosoftCommonProps=", StringComparison.Ordinal));
        Assert.Contains(args, a => a.StartsWith("-p:CustomAfterMicrosoftCommonTargets=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task iOSBuild_ImportsGeneratedPropsAndTargets()
    {
        using var workspace = TemporaryWorkspace.Create(outputHelper);
        var tempFile = Path.Combine(workspace.Path, "TempMauiProject.csproj");
        File.WriteAllText(tempFile, MauiTestHelper.CreateProjectContent("net10.0-ios"));

        var appBuilder = DistributedApplication.CreateBuilder();
        var maui = appBuilder.AddMauiProject("mauiapp", tempFile);
        var ios = maui.AddiOSSimulator()
            .WithEnvironment("MY_VAR", "hello");

        await using var app = appBuilder.Build();

        var args = await GetBuildArgumentsAsync(ios.Resource);

        Assert.Contains(args, a => a.StartsWith("-p:CustomBeforeMicrosoftCommonProps=", StringComparison.Ordinal));
        Assert.Contains(args, a => a.StartsWith("-p:CustomAfterMicrosoftCommonTargets=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WindowsBuild_ImportsGeneratedProps()
    {
        using var workspace = TemporaryWorkspace.Create(outputHelper);
        var tempFile = Path.Combine(workspace.Path, "TempMauiProject.csproj");
        File.WriteAllText(tempFile, MauiTestHelper.CreateProjectContent("net10.0-windows10.0.19041.0"));

        var appBuilder = DistributedApplication.CreateBuilder();
        var maui = appBuilder.AddMauiProject("mauiapp", tempFile);
        var windows = maui.AddWindowsDevice()
            .WithEnvironment("MY_VAR", "hello");

        await using var app = appBuilder.Build();

        var args = await GetBuildArgumentsAsync(windows.Resource);

        // Windows surfaces WithEnvironment values as MSBuild properties via the early props import. It has no
        // platform launch item hooks, so no targets file is generated.
        Assert.Contains(args, a => a.StartsWith("-p:CustomBeforeMicrosoftCommonProps=", StringComparison.Ordinal));
        Assert.DoesNotContain(args, a => a.StartsWith("-p:CustomAfterMicrosoftCommonTargets=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MacCatalystBuild_ImportsGeneratedProps()
    {
        using var workspace = TemporaryWorkspace.Create(outputHelper);
        var tempFile = Path.Combine(workspace.Path, "TempMauiProject.csproj");
        File.WriteAllText(tempFile, MauiTestHelper.CreateProjectContent("net10.0-maccatalyst"));

        var appBuilder = DistributedApplication.CreateBuilder();
        var maui = appBuilder.AddMauiProject("mauiapp", tempFile);
        var macCatalyst = maui.AddMacCatalystDevice()
            .WithEnvironment("MY_VAR", "hello");

        await using var app = appBuilder.Build();

        var args = await GetBuildArgumentsAsync(macCatalyst.Resource);

        // Mac Catalyst surfaces WithEnvironment values as MSBuild properties via the early props import. It has
        // no platform launch item hooks, so no targets file is generated.
        Assert.Contains(args, a => a.StartsWith("-p:CustomBeforeMicrosoftCommonProps=", StringComparison.Ordinal));
        Assert.DoesNotContain(args, a => a.StartsWith("-p:CustomAfterMicrosoftCommonTargets=", StringComparison.Ordinal));
    }

    private static async Task<List<string>> GetBuildArgumentsAsync(IResource resource)
    {
        var buildInfo = resource.Annotations.OfType<MauiBuildInfoAnnotation>().Last();
        return await MauiBuildQueueEventSubscriber.BuildDotnetBuildArgumentsAsync(resource, buildInfo, NullLogger.Instance, CancellationToken.None);
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
