using System.Diagnostics;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Adapters.Claude;
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

		var result = await CliProcess.Run(executable, arguments, Path.GetTempPath(), TimeSpan.FromSeconds(10), Token);

		result.ExitCode.ShouldBe(3);
		result.StandardOutput.Trim().ShouldBe("hello");
	}

	[Fact]
	public async Task A_child_that_keeps_the_output_open_after_the_cli_exits_is_a_timeout()
	{
		// The CLI exits at once, its background child inherits stdout and holds the pipe for a while.
		var (executable, arguments) = Shell("start /b ping -n 20 127.0.0.1", "sleep 20 &");
		var watch = Stopwatch.StartNew();

		var exception = await Should.ThrowAsync<ProviderException>(() => CliProcess.Run(executable, arguments, Path.GetTempPath(), TimeSpan.FromSeconds(1), Token));

		exception.Code.ShouldBe(ProviderErrorCodes.Timeout);
		watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
	}

	[Fact]
	public async Task A_command_still_running_at_the_deadline_is_killed_and_reported_as_a_timeout()
	{
		var (executable, arguments) = Shell("ping -n 20 127.0.0.1", "sleep 20");
		var watch = Stopwatch.StartNew();

		var exception = await Should.ThrowAsync<ProviderException>(() => CliProcess.Run(executable, arguments, Path.GetTempPath(), TimeSpan.FromSeconds(1), Token));

		exception.Code.ShouldBe(ProviderErrorCodes.Timeout);
		watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
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
