// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Xml.Linq;
using Aspire.Hosting.Maui.Utilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aspire.Hosting.Tests;

/// <summary>
/// Tests for MauiEnvironmentHelper utility methods including targets file generation,
/// semicolon encoding, and filename sanitization.
/// </summary>
public class MauiEnvironmentHelperTests
{
    [Fact]
    public void GenerateAndroidTargetsFileContent_ProducesValidXml()
    {
        var envVars = new Dictionary<string, string>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4317",
            ["MY_VAR"] = "hello"
        };

        var content = MauiEnvironmentHelper.GenerateAndroidTargetsFileContent(envVars);

        // Should be valid XML
        var doc = XDocument.Parse(content);
        Assert.NotNull(doc.Root);
        Assert.Equal("Project", doc.Root.Name.LocalName);
    }

    [Fact]
    public void GenerateAndroidTargetsFileContent_ContainsEnvironmentVariablesInItemGroup()
    {
        var envVars = new Dictionary<string, string>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4317",
            ["MY_VAR"] = "hello"
        };

        var content = MauiEnvironmentHelper.GenerateAndroidTargetsFileContent(envVars);
        var doc = XDocument.Parse(content);

        var items = doc.Descendants("_GeneratedAndroidEnvironment").ToList();
        Assert.Equal(2, items.Count);

        // Items should be ordered by key
        Assert.Equal("MY_VAR=hello", items[0].Attribute("Include")?.Value);
        Assert.Equal("OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317", items[1].Attribute("Include")?.Value);
    }

    [Fact]
    public void GenerateAndroidTargetsFileContent_ContainsAspireTargetDefinition()
    {
        var envVars = new Dictionary<string, string>
        {
            ["MY_VAR"] = "value"
        };

        var content = MauiEnvironmentHelper.GenerateAndroidTargetsFileContent(envVars);
        var doc = XDocument.Parse(content);

        var target = doc.Descendants("Target")
            .FirstOrDefault(t => t.Attribute("Name")?.Value == "AspireGenerateAndroidEnvironmentFiles");
        Assert.NotNull(target);
        Assert.Equal("_GenerateEnvironmentFiles", target.Attribute("BeforeTargets")?.Value);
    }

    [Fact]
    public void GenerateAndroidTargetsFileContent_EncodesEnvironmentFilePath()
    {
        var envVars = new Dictionary<string, string>
        {
            ["KEY"] = "value"
        };

        var content = MauiEnvironmentHelper.GenerateAndroidTargetsFileContent(envVars);
        var doc = XDocument.Parse(content);

        var writeLines = doc.Descendants("WriteLinesToFile").FirstOrDefault();
        Assert.NotNull(writeLines);
        Assert.Equal("$(IntermediateOutputPath)__aspire_environment__.txt", writeLines.Attribute("File")?.Value);
    }

    [Fact]
    public void GenerateAndroidTargetsFileContent_EncodesSemicolonsInValues()
    {
        var envVars = new Dictionary<string, string>
        {
            ["LITERAL_ESCAPE"] = "already%3Bencoded",
            ["PATH"] = "/usr/bin;/usr/local/bin"
        };

        var content = MauiEnvironmentHelper.GenerateAndroidTargetsFileContent(envVars);
        var doc = XDocument.Parse(content);

        var items = doc.Descendants("_GeneratedAndroidEnvironment")
            .Select(item => item.Attribute("Include")?.Value)
            .ToList();

        Assert.Equal(
            [
                "LITERAL_ESCAPE=already%253Bencoded",
                "PATH=/usr/bin%3B/usr/local/bin"
            ],
            items);
    }

    [Fact]
    public void GenerateiOSTargetsFileContent_ProducesValidXml()
    {
        var envVars = new Dictionary<string, string>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4317",
            ["MY_VAR"] = "hello"
        };

        var content = MauiEnvironmentHelper.GenerateiOSTargetsFileContent(envVars);

        var doc = XDocument.Parse(content);
        Assert.NotNull(doc.Root);
        Assert.Equal("Project", doc.Root.Name.LocalName);
    }

    [Fact]
    public void GenerateiOSTargetsFileContent_ContainsMlaunchEnvironmentVariables()
    {
        var envVars = new Dictionary<string, string>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4317",
            ["MY_VAR"] = "hello"
        };

        var content = MauiEnvironmentHelper.GenerateiOSTargetsFileContent(envVars);
        var doc = XDocument.Parse(content);

        var items = doc.Descendants("MlaunchEnvironmentVariables").ToList();
        Assert.Equal(2, items.Count);

        // Items should be ordered by key
        Assert.Equal("MY_VAR=hello", items[0].Attribute("Include")?.Value);
        Assert.Equal("OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317", items[1].Attribute("Include")?.Value);
    }

    [Fact]
    public void GenerateiOSTargetsFileContent_ContainsDiagnosticTarget()
    {
        var envVars = new Dictionary<string, string>
        {
            ["MY_VAR"] = "value"
        };

        var content = MauiEnvironmentHelper.GenerateiOSTargetsFileContent(envVars);
        var doc = XDocument.Parse(content);

        var target = doc.Descendants("Target")
            .FirstOrDefault(t => t.Attribute("Name")?.Value == "AspireLogMlaunchEnvironmentVariables");
        Assert.NotNull(target);
        Assert.Equal("PrepareForBuild", target.Attribute("AfterTargets")?.Value);
    }

    [Fact]
    public void GenerateiOSTargetsFileContent_EncodesSemicolonsInValues()
    {
        var envVars = new Dictionary<string, string>
        {
            ["LITERAL_ESCAPE"] = "already%3Bencoded",
            ["PATH"] = "/usr/bin;/usr/local/bin"
        };

        var content = MauiEnvironmentHelper.GenerateiOSTargetsFileContent(envVars);
        var doc = XDocument.Parse(content);

        var items = doc.Descendants("MlaunchEnvironmentVariables")
            .Select(item => item.Attribute("Include")?.Value)
            .ToList();

        Assert.Equal(
            [
                "LITERAL_ESCAPE=already%253Bencoded",
                "PATH=/usr/bin%3B/usr/local/bin"
            ],
            items);
    }

    [Fact]
    public void GenerateEnvironmentPropsFileContent_ExposesEnvironmentVariablesAsProperties()
    {
        var envVars = new Dictionary<string, string>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4317",
            ["MY_VAR"] = "hello"
        };

        var content = MauiEnvironmentHelper.GenerateEnvironmentPropsFileContent(envVars, NullLogger.Instance);
        var doc = XDocument.Parse(content);

        Assert.Equal("Project", doc.Root!.Name.LocalName);

        // The file also contains a PropertyGroup that recovers the user's original
        // CustomBeforeMicrosoftCommonProps; select the one that carries the environment variables.
        var propertyGroup = doc.Root.Elements("PropertyGroup").Single(pg => pg.Element("MY_VAR") is not null);
        Assert.Equal("hello", propertyGroup.Element("MY_VAR")?.Value);
        Assert.Equal("http://localhost:4317", propertyGroup.Element("OTEL_EXPORTER_OTLP_ENDPOINT")?.Value);
    }

    [Fact]
    public void GenerateAndroidTargetsFileContent_DoesNotContainPropertyGroup()
    {
        var envVars = new Dictionary<string, string>
        {
            ["MY_VAR"] = "hello"
        };

        var content = MauiEnvironmentHelper.GenerateAndroidTargetsFileContent(envVars);
        var doc = XDocument.Parse(content);

        // Properties are surfaced through the early .props file, not the late .targets file.
        Assert.Empty(doc.Descendants("PropertyGroup"));
    }

    [Fact]
    public void GenerateiOSTargetsFileContent_DoesNotContainPropertyGroup()
    {
        var envVars = new Dictionary<string, string>
        {
            ["MY_VAR"] = "hello"
        };

        var content = MauiEnvironmentHelper.GenerateiOSTargetsFileContent(envVars);
        var doc = XDocument.Parse(content);

        // Properties are surfaced through the early .props file, not the late .targets file.
        Assert.Empty(doc.Descendants("PropertyGroup"));
    }

    [Fact]
    public void AddEnvironmentPropertyGroup_EncodesInvalidPropertyNames()
    {
        var envVars = new Dictionary<string, string>
        {
            ["services:api:0"] = "http://localhost:5000",
            ["1LEADING_DIGIT"] = "value"
        };

        var projectElement = new XElement("Project");
        MauiEnvironmentHelper.AddEnvironmentPropertyGroup(projectElement, envVars, NullLogger.Instance);

        var propertyGroup = projectElement.Elements("PropertyGroup").Single();

        // ':' is invalid in an MSBuild property name and a leading digit requires a '_' prefix.
        Assert.Equal("http://localhost:5000", propertyGroup.Element("services_api_0")?.Value);
        Assert.Equal("value", propertyGroup.Element("_1LEADING_DIGIT")?.Value);
    }

    [Fact]
    public void AddEnvironmentPropertyGroup_CollidingNames_EmitsFirstAndDoesNotDuplicate()
    {
        // "services:api:0" and "services_api_0" both encode to "services_api_0", and MSBuild property
        // names are case-insensitive, so these three variables collapse to a single property. The first
        // in ordinal-ignore-case order wins (':' sorts before '_'); the rest are dropped (and logged)
        // rather than overwriting.
        var envVars = new Dictionary<string, string>
        {
            ["services:api:0"] = "colon",
            ["services_api_0"] = "underscore",
            ["SERVICES_API_0"] = "upper"
        };

        var projectElement = new XElement("Project");
        MauiEnvironmentHelper.AddEnvironmentPropertyGroup(projectElement, envVars, NullLogger.Instance);

        var properties = projectElement.Elements("PropertyGroup").Single().Elements().ToList();

        var property = Assert.Single(properties);
        Assert.Equal("services_api_0", property.Name.LocalName);
        Assert.Equal("colon", property.Value);
    }

    [Fact]
    public void AddEnvironmentPropertyGroup_SkipsReservedMSBuildPropertyNames()
    {
        // Reserved MSBuild property names cannot be redefined in a project file (MSB4004), so they must not
        // be emitted as properties. Non-reserved variables in the same call are still surfaced.
        var envVars = new Dictionary<string, string>
        {
            ["MSBuildProjectDirectory"] = "/should/not/be/emitted",
            ["MSBuildVersion"] = "99.99",
            // Reserved-name matching is case-insensitive, matching MSBuild's own comparison.
            ["msbuildtoolsversion"] = "1.0",
            ["MY_VAR"] = "hello"
        };

        var projectElement = new XElement("Project");
        MauiEnvironmentHelper.AddEnvironmentPropertyGroup(projectElement, envVars, NullLogger.Instance);

        var properties = projectElement.Elements("PropertyGroup").Single().Elements().ToList();

        var property = Assert.Single(properties);
        Assert.Equal("MY_VAR", property.Name.LocalName);
        Assert.Equal("hello", property.Value);
    }

    [Fact]
    public void AddEnvironmentPropertyGroup_EscapesMSBuildSyntaxInValues()
    {
        // Values containing MSBuild syntax must be surfaced literally, not expanded. MSBuild un-escapes the
        // %XX sequences when the property is consumed, so these encoded values yield the original text.
        var envVars = new Dictionary<string, string>
        {
            ["PROPERTY_REF"] = "$(Configuration)",
            ["ITEM_REF"] = "@(Compile)",
            ["LITERAL_PERCENT"] = "50%$(Foo)"
        };

        var projectElement = new XElement("Project");
        MauiEnvironmentHelper.AddEnvironmentPropertyGroup(projectElement, envVars, NullLogger.Instance);

        var propertyGroup = projectElement.Elements("PropertyGroup").Single();

        Assert.Equal("%24%28Configuration%29", propertyGroup.Element("PROPERTY_REF")?.Value);
        Assert.Equal("%40%28Compile%29", propertyGroup.Element("ITEM_REF")?.Value);
        // '%' is encoded first so the escapes introduced for '$'/'(' are not re-decoded by MSBuild.
        Assert.Equal("50%25%24%28Foo%29", propertyGroup.Element("LITERAL_PERCENT")?.Value);
    }

    [Theory]
    [InlineData("plain-value", "plain-value")]
    [InlineData("$(Configuration)", "%24%28Configuration%29")]
    [InlineData("@(Compile)", "%40%28Compile%29")]
    [InlineData("50%off", "50%25off")]
    [InlineData("a;b", "a%3Bb")]
    [InlineData("it's", "it%27s")]
    [InlineData("a*b?c", "a%2Ab%3Fc")]
    [InlineData("", "")]
    public void EscapeMSBuildPropertyValue_EncodesMetacharacters(string input, string expected)
    {
        var result = MauiEnvironmentHelper.EscapeMSBuildPropertyValue(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("simple-value", "simple-value", false)]
    [InlineData("has;semicolons", "has%3Bsemicolons", true)]
    [InlineData("multiple;semi;colons", "multiple%3Bsemi%3Bcolons", true)]
    [InlineData("literal%3Bescape", "literal%253Bescape", true)]
    [InlineData("literal%percent;and;semicolons", "literal%25percent%3Band%3Bsemicolons", true)]
    [InlineData("", "", false)]
    [InlineData("no-special-chars", "no-special-chars", false)]
    public void EncodeMSBuildItemValue_EncodesCorrectly(string input, string expectedOutput, bool expectedWasEncoded)
    {
        var result = MauiEnvironmentHelper.EncodeMSBuildItemValue(input, out var wasEncoded);

        Assert.Equal(expectedOutput, result);
        Assert.Equal(expectedWasEncoded, wasEncoded);
    }

    [Theory]
    [InlineData("simple-name", "simple-name")]
    [InlineData("name-with-dots.here", "name-with-dots.here")]
    [InlineData("valid_file_name", "valid_file_name")]
    public void SanitizeFileName_ValidNames_ReturnsUnchanged(string input, string expected)
    {
        var result = MauiEnvironmentHelper.SanitizeFileName(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SanitizeFileName_InvalidChars_ReplacesWithUnderscore()
    {
        // Build a test string with a printable character that's invalid on the current platform.
        // Skip '\0' since it has special behavior in .NET string comparisons.
        var invalidChar = Path.GetInvalidFileNameChars()
            .FirstOrDefault(c => c != '\0');
        if (invalidChar == '\0')
        {
            Assert.Skip("No printable invalid filename characters on this platform");
            return;
        }

        var input = $"name{invalidChar}test";
        var result = MauiEnvironmentHelper.SanitizeFileName(input);

        Assert.DoesNotContain(invalidChar.ToString(), result);
        Assert.Equal("name_test", result);
    }
}
