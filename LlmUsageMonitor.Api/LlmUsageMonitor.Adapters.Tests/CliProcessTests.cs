using System.Diagnostics;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.Adapters.Tests;

public sealed class CliProcessTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>A shell command, through cmd on Windows and sh elsewhere.</summary>
	private static (string Executable, string[] Arguments) Shell(string windows, string unix)
	{
		return OperatingSystem.IsWindows() ? ("cmd.exe", ["/d", "/c", windows]) : ("/bin/sh", ["-c", unix]);
	}

	[Fact]
	public async Task A_finished_command_returns_its_exit_code_and_output()
	{
		var (executable, arguments) = Shell("echo hello& exit 3", "echo hello; exit 3");

		var result = await CliProcess.Run(executable, arguments, Path.GetTempPath(), TimeSpan.FromSeconds(10), NullLogger.Instance, Token);

		result.ExitCode.ShouldBe(3);
		result.StandardOutput.Trim().ShouldBe("hello");
	}

	[Fact]
	public async Task A_child_that_keeps_the_output_open_after_the_cli_exits_is_a_timeout()
	{
		// The CLI exits at once, its background child inherits stdout and holds the pipe for a while.
		var (executable, arguments) = Shell("start /b ping -n 20 127.0.0.1", "sleep 20 &");
		var watch = Stopwatch.StartNew();

		var exception = await Should.ThrowAsync<ProviderException>(() => CliProcess.Run(executable, arguments, Path.GetTempPath(), TimeSpan.FromSeconds(1), NullLogger.Instance, Token));

		exception.Code.ShouldBe(ProviderErrorCode.Timeout);
		watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
	}

	[Fact]
	public async Task A_command_still_running_at_the_deadline_is_killed_and_reported_as_a_timeout()
	{
		var (executable, arguments) = Shell("ping -n 20 127.0.0.1", "sleep 20");
		var watch = Stopwatch.StartNew();

		var exception = await Should.ThrowAsync<ProviderException>(() => CliProcess.Run(executable, arguments, Path.GetTempPath(), TimeSpan.FromSeconds(1), NullLogger.Instance, Token));

		exception.Code.ShouldBe(ProviderErrorCode.Timeout);
		watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
	}

	[Fact]
	public async Task Each_run_is_logged_with_its_command_exit_code_duration_and_stderr()
	{
		var logger = new ListLogger();
		var (executable, arguments) = Shell("echo boom 1>&2& exit 2", "echo boom >&2; exit 2");

		await CliProcess.Run(executable, arguments, Path.GetTempPath(), TimeSpan.FromSeconds(10), logger, Token);

		logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("exited with code 2") && entry.Message.Contains(" ms"));
		logger.Entries.ShouldContain(entry => entry.Message.Contains("stderr: boom"));
	}

	[Fact]
	public async Task A_killed_run_is_logged()
	{
		var logger = new ListLogger();
		var (executable, arguments) = Shell("ping -n 20 127.0.0.1", "sleep 20");

		await Should.ThrowAsync<ProviderException>(() => CliProcess.Run(executable, arguments, Path.GetTempPath(), TimeSpan.FromSeconds(1), logger, Token));

		logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("killed after") && entry.Message.Contains("timed out"));
	}

	[Theory]
	[InlineData("error: unknown option '--safe-mode'", true)]
	[InlineData("error: unexpected argument '--listen' found", true)]
	[InlineData("error: unrecognized subcommand 'app-server'", true)]
	[InlineData("API Error: 529 Overloaded", false)]
	public void Argument_parser_errors_are_recognized(string output, bool expected)
	{
		CliProcess.IsUnsupportedOption(output).ShouldBe(expected);
	}

	[Fact]
	public void Killing_a_process_that_already_exited_is_not_an_error()
	{
		var (executable, arguments) = Shell("exit 0", "exit 0");
		var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
		foreach (var argument in arguments) info.ArgumentList.Add(argument);
		using var process = Process.Start(info)!;
		process.WaitForExit();

		Should.NotThrow(() => CliProcess.KillTree(process));
	}
}

internal sealed class ListLogger : ILogger
{
	public List<(LogLevel Level, string Message)> Entries { get; } = [];

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull
	{
		return null;
	}

	public bool IsEnabled(LogLevel logLevel)
	{
		return true;
	}

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		lock (Entries)
		{
			Entries.Add((logLevel, formatter(state, exception)));
		}
	}
}
