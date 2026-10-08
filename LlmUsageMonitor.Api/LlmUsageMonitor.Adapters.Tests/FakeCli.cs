using System.Text.Json;
using System.Text.Json.Nodes;

namespace LlmUsageMonitor.Adapters.Tests;

/// <summary>
///     A working directory holding the scenario of the fake CLI (LlmUsageMonitor.FakeCli), which the adapters run in place of
///     claude or codex. The fake records its invocations and the JSON-RPC messages it receives next to the scenario.
/// </summary>
internal sealed class FakeCli : IDisposable
{
	public FakeCli(JsonObject scenario)
	{
		System.IO.Directory.CreateDirectory(Directory);
		File.WriteAllText(Path.Combine(Directory, "fake-cli.json"), scenario.ToJsonString());
	}

	/// <summary>The fake executable, copied next to the tests by the CopyFakeCli target.</summary>
	public static string Executable { get; } = Path.Combine(AppContext.BaseDirectory, "fake-cli-bin", OperatingSystem.IsWindows() ? "fake-cli.exe" : "fake-cli");

	public string Directory { get; } = Path.Combine(Path.GetTempPath(), "llm-usage-monitor-tests", $"fake-cli-{Guid.NewGuid():N}");

	/// <summary>The arguments of each run, one line per run.</summary>
	public IReadOnlyList<string> Invocations => ReadLines("invocations.log");

	/// <summary>The JSON-RPC messages received by <c>app-server</c>, in order.</summary>
	public IReadOnlyList<JsonElement> Messages => ReadLines("requests.log").Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToList();

	public void Dispose()
	{
		try
		{
			System.IO.Directory.Delete(Directory, true);
		}
		catch (IOException)
		{
			// A killed process may still hold a log file for a moment: the temporary directory is left behind.
		}
	}

	/// <summary>A command answered with its output and exit code.</summary>
	public static JsonObject Command(string stdout = "", string stderr = "", int exitCode = 0)
	{
		return new() { ["stdout"] = stdout, ["stderr"] = stderr, ["exitCode"] = exitCode };
	}

	public static JsonObject Scenario(JsonObject? commands = null, JsonObject? rpc = null)
	{
		return new() { ["commands"] = commands ?? new JsonObject(), ["rpc"] = rpc ?? new JsonObject() };
	}

	private List<string> ReadLines(string name)
	{
		var path = Path.Combine(Directory, name);
		return File.Exists(path) ? File.ReadAllLines(path).Where(line => line.Length > 0).ToList() : [];
	}
}
