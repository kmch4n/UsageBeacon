using System.Diagnostics;
using System.IO;
using System.Text;

namespace UsageBeacon.Services;

internal interface IProcessCommandRunner
{
    Task<ProcessCommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        Encoding standardOutputEncoding,
        TimeSpan timeout,
        CancellationToken ct);
}

internal sealed record ProcessCommandResult(
    int ExitCode,
    string StandardOutput,
    bool TimedOut);

internal sealed class ProcessCommandRunner : IProcessCommandRunner
{
    public async Task<ProcessCommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        Encoding standardOutputEncoding,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = standardOutputEncoding,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            return new ProcessCommandResult(-1, "", TimedOut: false);

        var outputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            ct,
            timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
            var output = await outputTask;
            _ = await errorTask;
            return new ProcessCommandResult(process.ExitCode, output, TimedOut: false);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            await WaitForExitAfterKillAsync(process);
            _ = await ObserveAsync(outputTask);
            _ = await ObserveAsync(errorTask);

            ct.ThrowIfCancellationRequested();
            return new ProcessCommandResult(-1, "", TimedOut: true);
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static async Task WaitForExitAfterKillAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task<string> ObserveAsync(Task<string> task)
    {
        try
        {
            return await task;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            return "";
        }
    }
}
