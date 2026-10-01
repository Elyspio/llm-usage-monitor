using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using LlmUsageMonitor.Abstractions.Exceptions;

namespace LlmUsageMonitor.Adapters.Claude;

internal sealed record CliResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
///     Runs a CLI command with stdin closed (immediate EOF), a timeout and a kill of the whole process tree.
/// </summary>
internal static class CliProcess
{
	public static async Task<CliResult> Run(string executable, IEnumerable<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
	{
		var info = new ProcessStartInfo(executable)
		{
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
			WorkingDirectory = workingDirectory,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		foreach (var argument in arguments) info.ArgumentList.Add(argument);

		Process process;
		try
		{
			process = Process.Start(info) ?? throw new ProviderException(ProviderErrorCodes.CliUnavailable, $"Cannot start {executable}.");
		}
		catch (Win32Exception exception)
		{
			throw new ProviderException(ProviderErrorCodes.CliUnavailable, $"Cannot start {executable}. Install its CLI or fix the configured executable path.", exception);
		}

		using (process)
		{
			process.StandardInput.Close();
			var standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
			var standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);

			// One deadline for the process and its output: a child (an MCP server, for instance) may keep the pipes open after
			// the CLI exits, and reading to the end would then never finish.
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			deadline.CancelAfter(timeout);
			try
			{
				await process.WaitForExitAsync(deadline.Token);
				await Task.WhenAll(standardOutput, standardError).WaitAsync(deadline.Token);
			}
			catch (OperationCanceledException)
			{
				KillTree(process);
				if (cancellationToken.IsCancellationRequested)
				{
					throw;
				}

				throw new ProviderException(ProviderErrorCodes.Timeout, $"{executable} did not finish within {timeout.TotalSeconds:0} s.");
			}

			return new(process.ExitCode, await standardOutput, await standardError);
		}
	}

	/// <summary>
	///     Kills the process and its descendants; a process that already exited is not an error.
	/// </summary>
	public static void KillTree(Process process)
	{
		try
		{
			process.Kill(true);
		}
		catch (InvalidOperationException)
		{
			// Exited between the deadline and the kill.
		}
		catch (Win32Exception)
		{
			// A descendant exited while the tree was walked.
		}
	}
}
