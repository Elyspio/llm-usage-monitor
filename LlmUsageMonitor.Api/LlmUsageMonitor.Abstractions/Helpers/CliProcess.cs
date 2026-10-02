using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using LlmUsageMonitor.Abstractions.Exceptions;
using Microsoft.Extensions.Logging;

namespace LlmUsageMonitor.Abstractions.Helpers;

public sealed record CliResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
///     Runs a CLI command with stdin closed (immediate EOF), a timeout and a kill of the whole process tree. Each run is logged:
///     command, duration, exit code or kill, and the start of stderr.
/// </summary>
public static partial class CliProcess
{
	public const int LoggedOutputLength = 500;

	public static async Task<CliResult> Run(string executable, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, ILogger logger,
		CancellationToken cancellationToken)
	{
		var command = Describe(executable, arguments);
		var process = Start(executable, arguments, workingDirectory, logger);

		var watch = Stopwatch.StartNew();
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
				var reason = cancellationToken.IsCancellationRequested ? "cancelled" : "timed out";
				logger.LogWarning("{Command} killed after {DurationMs} ms ({Reason})", command, watch.ElapsedMilliseconds, reason);
				if (cancellationToken.IsCancellationRequested)
				{
					throw;
				}

				throw new ProviderException(ProviderErrorCodes.Timeout, $"{executable} did not finish within {timeout.TotalSeconds:0} s.");
			}

			var result = new CliResult(process.ExitCode, await standardOutput, await standardError);
			var level = result.ExitCode == 0 ? LogLevel.Information : LogLevel.Warning;
			logger.Log(level, "{Command} exited with code {ExitCode} in {DurationMs} ms", command, result.ExitCode, watch.ElapsedMilliseconds);
			if (!string.IsNullOrWhiteSpace(result.StandardError))
			{
				// stderr may hold account details: kept short, and only visible at debug level when the command succeeded.
				logger.Log(result.ExitCode == 0 ? LogLevel.Debug : LogLevel.Warning, "{Command} stderr: {StandardError}", command, Truncate(result.StandardError));
			}

			return result;
		}
	}

	/// <summary>
	///     Starts the CLI with its three standard streams redirected in UTF-8. Throws <c>CLI_UNAVAILABLE</c> when the executable
	///     cannot start; <paramref name="unavailableHint" /> tells how to fix it.
	/// </summary>
	public static Process Start(string executable, IReadOnlyList<string> arguments, string workingDirectory, ILogger logger,
		string unavailableHint = "Install its CLI or fix the configured executable path.")
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

		try
		{
			return Process.Start(info) ?? throw new ProviderException(ProviderErrorCodes.CliUnavailable, $"Cannot start {executable}.");
		}
		catch (Win32Exception exception)
		{
			logger.LogWarning("{Command} could not start: {Error}", Describe(executable, arguments), exception.Message);
			throw new ProviderException(ProviderErrorCodes.CliUnavailable, $"Cannot start {executable}. {unavailableHint}", exception);
		}
	}

	/// <summary>The command as logged: the executable name and the arguments.</summary>
	public static string Describe(string executable, IReadOnlyList<string> arguments)
	{
		return string.Join(' ', [Path.GetFileName(executable), .. arguments]);
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

	public static string Truncate(string text)
	{
		var trimmed = text.Trim();
		return trimmed.Length <= LoggedOutputLength ? trimmed : $"{trimmed[..LoggedOutputLength]}…";
	}

	/// <summary>
	///     The argument errors of the CLI parsers (commander for Claude, clap for Codex): a self-updated CLI dropped an option.
	/// </summary>
	public static bool IsUnsupportedOption(string text)
	{
		return UnsupportedOptionPattern().IsMatch(text);
	}

	/// <summary>
	///     The <c>--version</c> output of the CLI, for the message of an unsupported option; <c>unknown version</c> when it fails.
	/// </summary>
	public static async Task<string> ReadVersion(string executable, string workingDirectory, ILogger logger, CancellationToken cancellationToken)
	{
		try
		{
			var result = await Run(executable, ["--version"], workingDirectory, TimeSpan.FromSeconds(15), logger, cancellationToken);
			var version = result.StandardOutput.Trim();
			return result.ExitCode == 0 && version.Length > 0 ? Truncate(version) : "unknown version";
		}
		catch (ProviderException)
		{
			return "unknown version";
		}
	}

	[GeneratedRegex(@"unknown option|unexpected argument|unrecognized (option|argument|subcommand)|unknown argument|invalid option", RegexOptions.IgnoreCase)]
	private static partial Regex UnsupportedOptionPattern();
}
