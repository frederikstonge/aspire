// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma warning disable ASPIREFILESYSTEM001 // Type is for evaluation purposes only

using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Maui.Annotations;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting.Maui.Utilities;

/// <summary>
/// Provides utilities for handling environment variables in MAUI projects.
/// </summary>
/// <remarks>
/// Some MAUI platforms (like Android and iOS) require environment variables to be passed via
/// an intermediate MSBuild targets file rather than directly through the process environment.
/// This class provides reusable infrastructure for generating these targets files.
/// </remarks>
internal static class MauiEnvironmentHelper
{
    /// <summary>
    /// Creates a lazy generator annotation for an Android resource's environment files, capturing the
    /// services needed to generate them at launch time.
    /// </summary>
    /// <remarks>
    /// Attached at resource creation so both the serialized pre-build and the DCP launch command can share
    /// the same generated files regardless of eventing order. See <see cref="MauiEnvironmentFilesAnnotation"/>.
    /// </remarks>
    public static MauiEnvironmentFilesAnnotation CreateAndroidEnvironmentFilesAnnotation(IDistributedApplicationBuilder appBuilder, IResource resource)
    {
        var fileSystemService = appBuilder.FileSystemService;
        var executionContext = appBuilder.ExecutionContext;
        return new MauiEnvironmentFilesAnnotation((logger, ct) =>
            CreateAndroidEnvironmentFilesAsync(fileSystemService, resource, executionContext, logger, ct));
    }

    /// <summary>
    /// Creates a lazy generator annotation for an iOS resource's environment files, capturing the
    /// services needed to generate them at launch time.
    /// </summary>
    /// <remarks>
    /// Attached at resource creation so both the serialized pre-build and the DCP launch command can share
    /// the same generated files regardless of eventing order. See <see cref="MauiEnvironmentFilesAnnotation"/>.
    /// </remarks>
    public static MauiEnvironmentFilesAnnotation CreateiOSEnvironmentFilesAnnotation(IDistributedApplicationBuilder appBuilder, IResource resource)
    {
        var fileSystemService = appBuilder.FileSystemService;
        var executionContext = appBuilder.ExecutionContext;
        return new MauiEnvironmentFilesAnnotation((logger, ct) =>
            CreateiOSEnvironmentFilesAsync(fileSystemService, resource, executionContext, logger, ct));
    }

    /// <summary>
    /// Creates a lazy generator annotation for a resource's environment properties, capturing the services
    /// needed to resolve them at launch time. Unlike the Android and iOS variants, this produces only the
    /// global MSBuild property arguments (no platform-specific targets file), so it fits platforms such as
    /// Windows and Mac Catalyst that surface <c>WithEnvironment</c> values as MSBuild properties but do not
    /// need extra platform launch item hooks.
    /// </summary>
    /// <remarks>
    /// Attached at resource creation. Unlike Android and iOS, these platforms have no launch-command callback,
    /// so only the serialized pre-build consumes the generated properties. See <see cref="MauiEnvironmentFilesAnnotation"/>.
    /// </remarks>
    public static MauiEnvironmentFilesAnnotation CreateEnvironmentPropertyArgsAnnotation(IDistributedApplicationBuilder appBuilder, IResource resource)
    {
        var executionContext = appBuilder.ExecutionContext;
        return new MauiEnvironmentFilesAnnotation((logger, ct) =>
            CreateEnvironmentPropertyArgsAsync(resource, executionContext, logger, ct));
    }

    /// <summary>
    /// Resolves a resource's environment variables into global MSBuild property arguments, without generating
    /// any platform-specific targets file.
    /// </summary>
    /// <param name="resource">The resource to collect environment variables from.</param>
    /// <param name="executionContext">The execution context.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The <c>-p:NAME=VALUE</c> arguments exposing the environment variables as MSBuild properties and a null
    /// targets path, or an empty list if no environment variables are present.
    /// </returns>
    public static async Task<(IReadOnlyList<string> PropertyArgs, string? TargetsFilePath)> CreateEnvironmentPropertyArgsAsync(
        IResource resource,
        DistributedApplicationExecutionContext executionContext,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var executionConfiguration = await ExecutionConfigurationBuilder.Create(resource)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(executionContext, logger, cancellationToken)
            .ConfigureAwait(false);

        if (!executionConfiguration.EnvironmentVariables.Any())
        {
            return (Array.Empty<string>(), null);
        }

        var environmentVariables = executionConfiguration.EnvironmentVariables.ToDictionary();

        // Windows and Mac Catalyst have no platform launch item hooks, so only the global properties are needed.
        return (BuildEnvironmentPropertyArgs(environmentVariables, logger), null);
    }

    /// <summary>
    /// Creates the MSBuild files that expose an Android resource's environment variables both to the
    /// project build (as properties) and to the Android launch tooling (as <c>AndroidEnvironment</c> items).
    /// </summary>
    /// <param name="fileSystemService">The file system service for managing temp files.</param>
    /// <param name="resource">The resource to collect environment variables from.</param>
    /// <param name="executionContext">The execution context.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The <c>-p:NAME=VALUE</c> arguments exposing the environment variables as MSBuild properties and the path
    /// of the generated targets file, or an empty list and null if no environment variables are present. The
    /// targets file is imported late (via <c>CustomAfterMicrosoftCommonTargets</c>) so the Android launch item
    /// hooks run after the common targets.
    /// </returns>
    public static async Task<(IReadOnlyList<string> PropertyArgs, string? TargetsFilePath)> CreateAndroidEnvironmentFilesAsync(
        IFileSystemService fileSystemService,
        IResource resource,
        DistributedApplicationExecutionContext executionContext,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var executionConfiguration = await ExecutionConfigurationBuilder.Create(resource)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(executionContext, logger, cancellationToken)
            .ConfigureAwait(false);

        // Normalize all the environment variables for the resource. Semicolon encoding is applied
        // later in GenerateAndroidTargetsFileContent so it stays close to where the values are
        // emitted into MSBuild items (mirroring the iOS targets file generation).
        foreach (var envVar in executionConfiguration.EnvironmentVariables)
        {
            var normalizedKey = envVar.Key.ToUpperInvariant();
            environmentVariables[normalizedKey] = envVar.Value;
        }

        // If no environment variables, return an empty result
        if (environmentVariables.Count == 0)
        {
            return (Array.Empty<string>(), null);
        }

        // Create a temporary directory to hold the generated targets file. The directory is tracked by the
        // file system service and removed on app shutdown (honoring ASPIRE_PRESERVE_TEMP_FILES).
        var tempDirectory = fileSystemService.TempDirectory.CreateTempSubdirectory("aspire-maui-android-env").Path;

        var sanitizedName = SanitizeFileName(resource.Name + "-android");
        var uniqueId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

        var targetsFilePath = Path.Combine(tempDirectory, $"{sanitizedName}-{uniqueId}.targets");
        await File.WriteAllTextAsync(targetsFilePath, GenerateAndroidTargetsFileContent(environmentVariables), Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        return (BuildEnvironmentPropertyArgs(environmentVariables, logger), targetsFilePath);
    }

    /// <summary>
    /// Generates the content of an MSBuild targets file for Android environment variables.
    /// </summary>
    internal static string GenerateAndroidTargetsFileContent(Dictionary<string, string> environmentVariables)
    {
        var projectElement = new XElement("Project");

        // Import the standard Custom.After.Microsoft.Common.targets if it exists
        projectElement.Add(new XElement(
            "Import",
            new XAttribute("Project", "$(MSBuildExtensionsPath)/v$(MSBuildToolsVersion)/Custom.After.Microsoft.Common.targets"),
            new XAttribute("Condition", "Exists('$(MSBuildExtensionsPath)/v$(MSBuildToolsVersion)/Custom.After.Microsoft.Common.targets')")
        ));

        // Create an ItemGroup for AndroidEnvironment files to be generated
        var itemGroup = new XElement("ItemGroup");
        foreach (var (key, value) in environmentVariables.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
        {
            var encodedValue = EncodeMSBuildItemValue(value, out _);
            itemGroup.Add(new XElement("_GeneratedAndroidEnvironment", new XAttribute("Include", $"{key}={encodedValue}")));
        }
        projectElement.Add(itemGroup);

        // Add target to generate environment file(s)
        var targetElement = new XElement(
            "Target",
            new XAttribute("Name", "AspireGenerateAndroidEnvironmentFiles"),
            new XAttribute("BeforeTargets", "_GenerateEnvironmentFiles"),
            new XAttribute("Condition", "'@(_GeneratedAndroidEnvironment)' != ''")
        );

        // Write environment variables to a temporary file in IntermediateOutputPath
        targetElement.Add(new XElement(
            "WriteLinesToFile",
            new XAttribute("File", "$(IntermediateOutputPath)__aspire_environment__.txt"),
            new XAttribute("Lines", "@(_GeneratedAndroidEnvironment)"),
            new XAttribute("Overwrite", "True"),
            new XAttribute("WriteOnlyWhenDifferent", "True")
        ));

        // Add the file to AndroidEnvironment items
        targetElement.Add(new XElement(
            "ItemGroup",
            new XElement("AndroidEnvironment", new XAttribute("Include", "$(IntermediateOutputPath)__aspire_environment__.txt"))
        ));

        // Add the file to FileWrites for clean
        targetElement.Add(new XElement(
            "ItemGroup",
            new XElement("FileWrites", new XAttribute("Include", "$(IntermediateOutputPath)__aspire_environment__.txt"))
        ));

        // Force the GeneratePackageManagerJava target to re-run by deleting its stamp file
        targetElement.Add(new XElement(
            "Delete",
            new XAttribute("Files", "$(_AndroidStampDirectory)_GeneratePackageManagerJava.stamp")
        ));

        projectElement.Add(targetElement);

        return SerializeProject(projectElement);
    }

    internal static string SanitizeFileName(string name)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        if (name.IndexOfAny(invalidCharacters) < 0)
        {
            return name;
        }

        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalidCharacters, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    /// <summary>
    /// Builds the <c>-p:NAME=VALUE</c> MSBuild command-line arguments that expose the environment variables as
    /// MSBuild properties. Global (command-line) properties are used instead of a generated props file imported
    /// through <c>CustomBeforeMicrosoftCommonProps</c>: that slot is a single-file extension point, so claiming
    /// it would replace (and there is no reliable way to recover) a user-supplied value — for example one set in
    /// a <c>Directory.Build.rsp</c> response file, which becomes an opaque global property. Global properties are
    /// also evaluated before any import, so the values are visible to project-level property definitions and
    /// conditions just like an early props import would be.
    /// </summary>
    internal static List<string> BuildEnvironmentPropertyArgs(Dictionary<string, string> environmentVariables, ILogger logger)
    {
        var args = new List<string>();
        foreach (var (name, value) in EnumerateEmittableProperties(environmentVariables, logger))
        {
            args.Add($"-p:{name}={value}");
        }

        return args;
    }

    // MSBuild's reserved (read-only) property names. Defining any of these in a project file throws MSB4004
    // ("The name '...' is reserved, and cannot be modified."), which would break the build for an otherwise
    // valid WithEnvironment call. Membership is exact (case-insensitive), not prefix based: MSBuild only
    // reserves this specific set, so custom "MSBuild"-prefixed names remain settable.
    // Mirrors Microsoft.Build.Internal.ReservedPropertyNames.ReservedProperties:
    // https://github.com/dotnet/msbuild/blob/main/src/Build/Resources/Constants.cs
    private static readonly HashSet<string> s_reservedMSBuildPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "MSBuildProjectDirectory",
        "MSBuildProjectDirectoryNoRoot",
        "MSBuildProjectFile",
        "MSBuildProjectExtension",
        "MSBuildProjectFullPath",
        "MSBuildProjectName",
        "MSBuildThisFileDirectory",
        "MSBuildThisFileDirectoryNoRoot",
        "MSBuildThisFile",
        "MSBuildThisFileExtension",
        "MSBuildThisFileFullPath",
        "MSBuildThisFileName",
        "MSBuildBinPath",
        "MSBuildProjectDefaultTargets",
        "MSBuildToolsPath",
        "MSBuildToolsVersion",
        "MSBuildRuntimeType",
        "MSBuildStartupDirectory",
        "MSBuildNodeCount",
        "MSBuildLastTaskResult",
        "MSBuildProgramFiles32",
        "MSBuildAssemblyVersion",
        "MSBuildVersion",
        "MSBuildInteractive",
        "MSBuildDisableFeaturesFromVersion",
    };

    /// <summary>
    /// Adds a <c>PropertyGroup</c> that exposes each environment variable as an MSBuild property so the
    /// values injected by Aspire are visible to the project build itself (for example in <c>$(NAME)</c>
    /// references or property conditions), not just to the platform launch tooling.
    /// </summary>
    /// <remarks>
    /// The emitted names/values follow the rules described on <see cref="EnumerateEmittableProperties"/>.
    /// XML special characters in the value are escaped automatically by <see cref="XElement"/>.
    /// </remarks>
    internal static void AddEnvironmentPropertyGroup(XElement projectElement, Dictionary<string, string> environmentVariables, ILogger logger)
    {
        var propertyGroup = new XElement("PropertyGroup");

        foreach (var (name, value) in EnumerateEmittableProperties(environmentVariables, logger))
        {
            propertyGroup.Add(new XElement(name, value));
        }

        projectElement.Add(propertyGroup);
    }

    /// <summary>
    /// Yields the encoded property name and escaped value for each environment variable that can be surfaced
    /// as an MSBuild property, in a stable ordinal-ignore-case order.
    /// </summary>
    /// <remarks>
    /// Environment variable names are encoded to valid MSBuild property identifiers via
    /// <see cref="EncodeMSBuildPropertyName(string)"/>: names that are already valid are used unchanged,
    /// otherwise invalid characters are replaced with '_'. Unlike a plain environment-variable encoder this
    /// keeps hyphens, because MSBuild allows '-' after the first character of a property name (for example
    /// <c>$(MY-VAR)</c>), so a hyphenated variable maps to the matching property instead of collapsing to an
    /// underscore. Because that encoding (and MSBuild's case-insensitive property names) can still map two
    /// distinct variables to the same property name, collisions are detected and only the first variable is
    /// emitted; the rest are logged rather than silently overwriting each other. Names that map to a reserved
    /// MSBuild property are skipped (and logged) so they do not fail the build with MSB4004. Values are escaped
    /// via <see cref="EscapeMSBuildPropertyValue"/> so MSBuild syntax in a value (such as
    /// <c>$(Configuration)</c>) is preserved literally instead of expanding.
    /// </remarks>
    internal static IEnumerable<(string Name, string Value)> EnumerateEmittableProperties(Dictionary<string, string> environmentVariables, ILogger logger)
    {
        // MSBuild property names are case-insensitive, and EncodeMSBuildPropertyName maps invalid characters
        // to '_', so two distinct variables can collide (e.g. "services:api:0" and "services_api_0", or "Foo"
        // and "FOO"). Track emitted names case-insensitively and skip later collisions so MSBuild does not
        // silently take the last definition. The colliding variables still reach the app via the launch items,
        // which use the original (unencoded) names.
        var emittedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in environmentVariables.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
        {
            var propertyName = EncodeMSBuildPropertyName(key);

            // An empty encoded name only occurs for an empty key, which cannot form a valid MSBuild
            // property (or XML element) name, so skip it.
            if (string.IsNullOrEmpty(propertyName))
            {
                continue;
            }

            // Reserved names (e.g. MSBuildProjectDirectory, MSBuildVersion) cannot be redefined in a project
            // file; emitting them would fail the whole build with MSB4004. Skip them so an otherwise valid
            // WithEnvironment call still works — the value is still passed to the app via the launch items.
            if (s_reservedMSBuildPropertyNames.Contains(propertyName))
            {
                logger.LogWarning(
                    "Environment variable '{Key}' maps to reserved MSBuild property '{PropertyName}', which cannot be redefined in a project file. " +
                    "Its value is not surfaced as an MSBuild property (it is still passed to the app).",
                    key,
                    propertyName);
                continue;
            }

            if (!emittedNames.Add(propertyName))
            {
                logger.LogWarning(
                    "Environment variable '{Key}' maps to MSBuild property '{PropertyName}', which is already defined by another variable. " +
                    "Its value is not surfaced as an MSBuild property (it is still passed to the app). Rename the variable to avoid the collision.",
                    key,
                    propertyName);
                continue;
            }

            yield return (propertyName, EscapeMSBuildPropertyValue(value));
        }
    }

    /// <summary>
    /// Encodes an environment variable name into a valid MSBuild property name.
    /// </summary>
    /// <remarks>
    /// MSBuild property names must start with a letter or underscore and may then contain letters, digits,
    /// underscores and hyphens (for example <c>$(MY-VAR)</c> is valid). Unlike a plain environment-variable
    /// encoder, hyphens are preserved so a hyphenated variable maps to the matching MSBuild property rather
    /// than collapsing to '_' (which could collide with a differently spelled variable). Any other character
    /// is replaced with '_', and a leading character that is not a letter or underscore is prefixed with '_'.
    /// </remarks>
    internal static string EncodeMSBuildPropertyName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var builder = new StringBuilder(name.Length + 1);

        // A valid MSBuild property name cannot start with a digit or hyphen, so prefix '_' when it would.
        if (!char.IsAsciiLetter(name[0]) && name[0] != '_')
        {
            builder.Append('_');
        }

        foreach (var c in name)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_');
        }

        return builder.ToString();
    }

    internal static string EncodeMSBuildItemValue(string value, out bool wasEncoded)
    {
        wasEncoded = value.Contains('%', StringComparison.Ordinal) || value.Contains(';', StringComparison.Ordinal);
        if (!wasEncoded)
        {
            return value;
        }

        // MSBuild item Include values use %-escaped sequences. Escape existing '%' first so a literal
        // value like "foo%3Bbar" is preserved as "%253B" instead of being decoded into "foo;bar".
        return value
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace(";", "%3B", StringComparison.Ordinal);
    }

    /// <summary>
    /// Escapes MSBuild metacharacters in an environment value so it is surfaced as a literal MSBuild property
    /// value rather than being interpreted as MSBuild syntax (for example a value of <c>$(Configuration)</c>
    /// or <c>@(Compile)</c> must not expand).
    /// </summary>
    /// <remarks>
    /// MSBuild un-escapes <c>%XX</c> hex sequences when a property is consumed, so encoding the special
    /// characters here yields the original literal value at use time. <c>%</c> is encoded first so the escapes
    /// introduced for the other characters are not themselves re-decoded.
    /// </remarks>
    internal static string EscapeMSBuildPropertyValue(string value)
    {
        // Matches MSBuild's escaping set (Microsoft.Build.Shared.EscapingUtilities): the characters that carry
        // syntactic meaning in an MSBuild property value.
        if (value.AsSpan().IndexOfAny("%$@();'*?") < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '%':
                    builder.Append("%25");
                    break;
                case '$':
                    builder.Append("%24");
                    break;
                case '@':
                    builder.Append("%40");
                    break;
                case '(':
                    builder.Append("%28");
                    break;
                case ')':
                    builder.Append("%29");
                    break;
                case ';':
                    builder.Append("%3B");
                    break;
                case '\'':
                    builder.Append("%27");
                    break;
                case '*':
                    builder.Append("%2A");
                    break;
                case '?':
                    builder.Append("%3F");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Creates the MSBuild files that expose an iOS resource's environment variables both to the project
    /// build (as properties) and to the iOS launch tooling (as <c>MlaunchEnvironmentVariables</c> items).
    /// </summary>
    /// <param name="fileSystemService">The file system service for managing temp files.</param>
    /// <param name="resource">The resource to collect environment variables from.</param>
    /// <param name="executionContext">The execution context.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The <c>-p:NAME=VALUE</c> arguments exposing the environment variables as MSBuild properties and the path
    /// of the generated targets file, or an empty list and null if no environment variables are present. The
    /// targets file is imported late (via <c>CustomAfterMicrosoftCommonTargets</c>) so the mlaunch item hooks
    /// run after the common targets.
    /// </returns>
    public static async Task<(IReadOnlyList<string> PropertyArgs, string? TargetsFilePath)> CreateiOSEnvironmentFilesAsync(
        IFileSystemService fileSystemService,
        IResource resource,
        DistributedApplicationExecutionContext executionContext,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var executionConfiguration = await ExecutionConfigurationBuilder.Create(resource)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(executionContext, logger, cancellationToken)
            .ConfigureAwait(false);

        // If no environment variables, return an empty result
        if (!executionConfiguration.EnvironmentVariables.Any())
        {
            return (Array.Empty<string>(), null);
        }

        var environmentVariables = executionConfiguration.EnvironmentVariables.ToDictionary();

        // Create a temporary directory to hold the generated targets file. The directory is tracked by the
        // file system service and removed on app shutdown (honoring ASPIRE_PRESERVE_TEMP_FILES).
        var tempDirectory = fileSystemService.TempDirectory.CreateTempSubdirectory("aspire-maui-mlaunch-env").Path;

        var sanitizedName = SanitizeFileName(resource.Name + "-ios");
        var uniqueId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

        var targetsFilePath = Path.Combine(tempDirectory, $"{sanitizedName}-{uniqueId}.targets");
        await File.WriteAllTextAsync(targetsFilePath, GenerateiOSTargetsFileContent(environmentVariables), Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        return (BuildEnvironmentPropertyArgs(environmentVariables, logger), targetsFilePath);
    }

    /// <summary>
    /// Generates the content of an MSBuild targets file for iOS environment variables.
    /// </summary>
    internal static string GenerateiOSTargetsFileContent(Dictionary<string, string> environmentVariables)
    {
        var projectElement = new XElement("Project");

        // Import the standard Custom.After.Microsoft.Common.targets if it exists
        projectElement.Add(new XElement(
            "Import",
            new XAttribute("Project", "$(MSBuildExtensionsPath)/v$(MSBuildToolsVersion)/Custom.After.Microsoft.Common.targets"),
            new XAttribute("Condition", "Exists('$(MSBuildExtensionsPath)/v$(MSBuildToolsVersion)/Custom.After.Microsoft.Common.targets')")
        ));

        // Create an ItemGroup to add environment variables using MlaunchEnvironmentVariables
        // iOS apps need environment variables passed to mlaunch as KEY=VALUE pairs
        var itemGroup = new XElement("ItemGroup");

        foreach (var (key, value) in environmentVariables.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            var encodedValue = EncodeMSBuildItemValue(value, out _);

            // Add as MlaunchEnvironmentVariables item with Include="KEY=VALUE"
            itemGroup.Add(new XElement("MlaunchEnvironmentVariables",
                new XAttribute("Include", $"{key}={encodedValue}")));
        }

        projectElement.Add(itemGroup);

        // Add a diagnostic message target to show what's being forwarded
        projectElement.Add(new XElement(
            "Target",
            new XAttribute("Name", "AspireLogMlaunchEnvironmentVariables"),
            new XAttribute("AfterTargets", "PrepareForBuild"),
            new XAttribute("Condition", "'@(MlaunchEnvironmentVariables)' != ''"),
            new XElement(
                "Message",
                new XAttribute("Importance", "High"),
                new XAttribute("Text", "Aspire forwarding mlaunch environment variables: @(MlaunchEnvironmentVariables, ', ')")
            )
        ));

        return SerializeProject(projectElement);
    }

    // MSBuild reads these files as UTF-8 (both callers write them with Encoding.UTF8). XDocument.Save emits an
    // XML declaration matching the writer's UTF-16 encoding, which would mislabel the persisted UTF-8 bytes and
    // can make MSBuild reject the import, so omit the declaration entirely (it is optional for MSBuild files).
    private static string SerializeProject(XElement projectElement)
    {
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = true,
        };

        using var stringWriter = new StringWriter();
        using (var xmlWriter = XmlWriter.Create(stringWriter, settings))
        {
            projectElement.Save(xmlWriter);
        }

        return stringWriter.ToString();
    }
}
