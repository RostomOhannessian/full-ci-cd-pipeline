using System.Diagnostics;

namespace Governance.Auditor.Common;

/// <summary>A program to run, with arguments passed as a list. No shell is involved, so an argument is never parsed as a command.</summary>
internal sealed record ProcessRequest(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory, string? StandardInput = null);

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>The one way the tool starts another program. Tests replace it, so no test needs Git or the GitHub CLI for logic checks.</summary>
internal interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
}

internal sealed class SystemProcessRunner : IProcessRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(request.FileName)
        {
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = request.StandardInput is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new GovernanceException($"Could not start '{request.FileName}'. Is it installed and on the PATH?", exception);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);

            if (request.StandardInput is not null)
            {
                await process.StandardInput.WriteAsync(request.StandardInput.AsMemory(), timeout.Token);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(timeout.Token);
            return new ProcessResult(process.ExitCode, await output, await error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new GovernanceException($"'{request.FileName}' did not finish within {Timeout.TotalSeconds:0} seconds.");
        }
    }
}
