using System.Diagnostics;
using System.Text;

namespace LangGrader.Services;

public sealed class CommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        int timeoutSeconds = 30)
    {
        using var process = new Process();

        process.StartInfo.FileName = fileName;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            process.StartInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var arg in arguments)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new CommandResult
            {
                ExitCode = -1,
                StandardOutput = "",
                StandardError = ex.Message,
                TimedOut = false
            };
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var completedTask = await Task.WhenAny(
            process.WaitForExitAsync(),
            Task.Delay(TimeSpan.FromSeconds(timeoutSeconds))
        );

        if (completedTask != process.WaitForExitAsync() && !process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore process kill failures.
            }

            return new CommandResult
            {
                ExitCode = -1,
                StandardOutput = stdout.ToString(),
                StandardError = stderr.ToString(),
                TimedOut = true
            };
        }

        return new CommandResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = stdout.ToString(),
            StandardError = stderr.ToString(),
            TimedOut = false
        };
    }
}