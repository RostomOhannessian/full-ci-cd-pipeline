using Xunit;

namespace Catalog.Architecture.Tests;

/// <summary>
/// The conditions under which the license exception for the native Windows runtime of SqlClient stops being valid, as far as the repository
/// can check them today (ADR-0004, amendment of 2026-11-03). The Linux publish output itself is checked by tools/ci/Test-NoWindowsNativeRuntime.ps1,
/// which the image build of WP2.1 runs against the real output (planned).
/// </summary>
public sealed class WindowsArtifactPolicyTests
{
    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public void No_production_project_selects_a_windows_runtime_identifier()
    {
        var files = WindowsArtifactPolicy.ProductionBuildFiles();

        // An empty list would make the check pass without reading anything.
        Assert.Contains("src/Catalog.Api/Catalog.Api.csproj", files);
        Assert.Contains("Directory.Build.props", files);

        var violations = files.SelectMany(file => WindowsArtifactPolicy.WindowsRuntimeIdentifiers(File.ReadAllText(Path.Combine(SolutionLayout.Root, file)))
            .Select(identifier => $"{file}: selects the runtime identifier {identifier}."))
            .ToList();

        Assert.Empty(violations);
    }

    [Theory]
    [Trait("Requirement", "REQ-SUP-003")]
    [InlineData("<Project><PropertyGroup><RuntimeIdentifier>win-x64</RuntimeIdentifier></PropertyGroup></Project>", 1)]
    [InlineData("<Project><PropertyGroup><RuntimeIdentifiers>linux-x64;win-arm64</RuntimeIdentifiers></PropertyGroup></Project>", 1)]
    [InlineData("<Project><PropertyGroup><RuntimeIdentifiers>win-x64;win-x86</RuntimeIdentifiers></PropertyGroup></Project>", 2)]
    [InlineData("<Project><PropertyGroup><RuntimeIdentifier>WIN10-x64</RuntimeIdentifier></PropertyGroup></Project>", 1)]
    [InlineData("<Project><PropertyGroup><RuntimeIdentifier>linux-x64</RuntimeIdentifier></PropertyGroup></Project>", 0)]
    [InlineData("<Project><PropertyGroup><RuntimeIdentifiers>linux-x64;linux-musl-x64;linux-arm64</RuntimeIdentifiers></PropertyGroup></Project>", 0)]
    [InlineData("<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>", 0)]
    public void The_project_scan_flags_exactly_the_windows_runtime_identifiers(string project, int expected)
    {
        Assert.Equal(expected, WindowsArtifactPolicy.WindowsRuntimeIdentifiers(project).Count);
    }

    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public void No_dockerfile_builds_on_a_windows_image()
    {
        var dockerfiles = WindowsArtifactPolicy.Dockerfiles();

        // The Dev Container Dockerfile exists today, so an empty list means the scan reads nothing.
        Assert.Contains(".devcontainer/Dockerfile", dockerfiles);

        var violations = dockerfiles.SelectMany(file => WindowsArtifactPolicy.WindowsBaseImages(File.ReadAllText(Path.Combine(SolutionLayout.Root, file)))
            .Select(line => $"{file}: {line}"))
            .ToList();

        Assert.Empty(violations);
    }

    [Theory]
    [Trait("Requirement", "REQ-SUP-003")]
    [InlineData("FROM mcr.microsoft.com/windows/nanoserver:ltsc2025", 1)]
    [InlineData("FROM mcr.microsoft.com/windows/servercore:ltsc2025 AS build", 1)]
    [InlineData("FROM mcr.microsoft.com/dotnet/aspnet:10.0-nanoserver-ltsc2025", 1)]
    [InlineData("FROM --platform=windows/amd64 mcr.microsoft.com/dotnet/sdk:10.0", 1)]
    [InlineData("from mcr.microsoft.com/dotnet/runtime:10.0-windowsservercore-ltsc2025", 1)]
    [InlineData("FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build\nRUN dotnet publish\nFROM mcr.microsoft.com/windows/nanoserver:ltsc2025", 1)]
    [InlineData("FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled@sha256:0123", 0)]
    [InlineData("FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build\nRUN echo windows", 0)]
    public void The_dockerfile_scan_flags_exactly_the_windows_base_images(string dockerfile, int expected)
    {
        Assert.Equal(expected, WindowsArtifactPolicy.WindowsBaseImages(dockerfile).Count);
    }
}
