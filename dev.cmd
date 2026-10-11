@echo off
rem The Windows shim for the dev command. It needs only the .NET SDK, because PowerShell 7 is a local .NET tool
rem that "dotnet tool restore" installs from .config\dotnet-tools.json. Everything else is in scripts\dev.ps1.
setlocal
set "ROOT=%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo dev: the .NET SDK is not installed. Install the SDK that global.json pins, or open the Dev Container. 1>&2
  exit /b 127
)

dotnet tool restore --tool-manifest "%ROOT%.config\dotnet-tools.json" >nul
if errorlevel 1 exit /b %errorlevel%

dotnet pwsh -NoProfile -File "%ROOT%scripts\dev.ps1" %*
exit /b %errorlevel%
