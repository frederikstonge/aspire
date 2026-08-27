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
/// which only inspects the helper output, these tests drive the subscriber's command-line callback so that a
/// regression which stops passing the injected environment values (as global <c>-p:</c> properties) or which
/// starts stealing the user's <c>CustomBeforeMicrosoftCommonProps</c> slot is caught.
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

        // Environment values are passed as global MSBuild properties (never via the CustomBeforeMicrosoftCommonProps
        // slot); the targets file is still imported late so the Android launch items reach the build.
        Assert.Contains("-p:MY_VAR=hello", args);
        Assert.Equal(new[] { "-p:CustomAfterMicrosoftCommonTargets" }, GetCustomImportArgKeys(args));
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

        // Environment values are passed as global MSBuild properties (never via the CustomBeforeMicrosoftCommonProps
        // slot); the targets file is still imported late so the mlaunch launch items reach the build.
        Assert.Contains("-p:MY_VAR=hello", args);
        Assert.Equal(new[] { "-p:CustomAfterMicrosoftCommonTargets" }, GetCustomImportArgKeys(args));
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

        // The actual `dotnet build` (not just the launch command) must carry the environment values as global
        // properties and import the targets file, otherwise build-time conditions and Android environment items
        // never see WithEnvironment values.
        Assert.Contains("-p:MY_VAR=hello", args);
        Assert.Equal(new[] { "-p:CustomAfterMicrosoftCommonTargets" }, GetCustomImportArgKeys(args));
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

        Assert.Contains("-p:MY_VAR=hello", args);
        Assert.Equal(new[] { "-p:CustomAfterMicrosoftCommonTargets" }, GetCustomImportArgKeys(args));
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

        // Windows surfaces WithEnvironment values as global MSBuild properties. It has no platform launch item
        // hooks, so no targets file is generated and the user's props extension slot is never touched.
        Assert.Contains("-p:MY_VAR=hello", args);
        Assert.Empty(GetCustomImportArgKeys(args));
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

        // Mac Catalyst surfaces WithEnvironment values as global MSBuild properties. It has no platform launch
        // item hooks, so no targets file is generated and the user's props extension slot is never touched.
        Assert.Contains("-p:MY_VAR=hello", args);
        Assert.Empty(GetCustomImportArgKeys(args));
    }

    private static async Task<List<string>> GetBuildArgumentsAsync(IResource resource)
    {
        var buildInfo = resource.Annotations.OfType<MauiBuildInfoAnnotation>().Last();
        return await MauiBuildQueueEventSubscriber.BuildDotnetBuildArgumentsAsync(resource, buildInfo, NullLogger.Instance, CancellationToken.None);
    }

    // Returns the ordered set of "-p:Custom..." MSBuild extension-import argument keys (without their
    // generated file paths) so tests can assert the complete set of imports rather than an absence.
    private static string[] GetCustomImportArgKeys(IEnumerable<string> args) =>
        args
            .Where(a => a.StartsWith("-p:Custom", StringComparison.Ordinal))
            .Select(a => a.Split('=', 2)[0])
            .OrderBy(a => a, StringComparer.Ordinal)
            .ToArray();

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
