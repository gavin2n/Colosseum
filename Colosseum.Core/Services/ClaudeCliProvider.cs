using System.Diagnostics;
using Colosseum.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Colosseum.Core.Services;

/// <summary>
/// Claude provider that shells out to the <c>claude</c> CLI (Claude Code).
/// Requires the claude CLI to be installed and authenticated.
/// The <c>--model</c> flag selects the model; max_tokens is not directly
/// controllable via the CLI and uses the model's defaults.
/// </summary>
public class ClaudeCliProvider(
    IOptions<ColosseumOptions> options,
    ILogger<ClaudeCliProvider> logger) : IClaudeProvider
{
    private readonly ColosseumOptions _opts = options.Value;

    public async Task<string> CompleteAsync(
        string prompt, string model, int maxTokens, CancellationToken ct = default)
    {
        var binary = _opts.CliBinaryPath;

        var psi = new ProcessStartInfo
        {
            FileName = binary,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Use ArgumentList so the shell never touches the values — no injection risk.
        psi.ArgumentList.Add("--model");
        psi.ArgumentList.Add(model);
        psi.ArgumentList.Add("--print");   // non-interactive print mode, reads prompt from stdin

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to start Claude CLI '{binary}'. " +
                "Ensure the claude CLI is installed and on your PATH (or set Colosseum:CliBinaryPath).", ex);
        }

        // Link the caller's token with a per-call timeout so a hung process never blocks forever.
        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(_opts.ClaudeCallTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var linkedToken = linkedCts.Token;

        // Kill the child process tree whenever cancellation fires (timeout or caller cancel).
        await using var killReg = linkedToken.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* process may have already exited */ }
        });

        // Write prompt to stdin, then close to signal EOF
        await process.StandardInput.WriteAsync(prompt.AsMemory(), linkedToken);
        process.StandardInput.Close();

        var outputTask = process.StandardOutput.ReadToEndAsync(linkedToken);
        var errorTask = process.StandardError.ReadToEndAsync(linkedToken);

        await process.WaitForExitAsync(linkedToken);

        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            logger.LogError("Claude CLI exited {Code}. stderr: {Err}", process.ExitCode, error);
            throw new InvalidOperationException(
                $"Claude CLI exited with code {process.ExitCode}: {error.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(error))
            logger.LogDebug("Claude CLI stderr: {Err}", error);

        return output;
    }
}
