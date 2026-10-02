using LlmUsageMonitor.Hosting;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.WebApi.Tests;

public sealed class SettingsFileTests : IDisposable
{
	private const string EnvironmentPrefix = "LLM_USAGE_MONITOR_SETTINGS_TESTS_";

	private readonly string _directory = Directory.CreateTempSubdirectory("llm-usage-monitor-settings-").FullName;

	public void Dispose()
	{
		Environment.SetEnvironmentVariable($"{EnvironmentPrefix}Oidc__Authority", null);
		Directory.Delete(_directory, true);
	}

	[Fact]
	public void The_production_file_overrides_the_appsettings_but_not_the_environment_nor_the_command_line()
	{
		var appsettings = WriteJson("appsettings.json", """{ "Oidc": { "Authority": "appsettings", "ClientId": "appsettings" }, "App": { "PublicUrl": "appsettings" }, "Urls": "appsettings" }""");
		var production = WriteJson("appsettings.Production.json",
			"""{ "Oidc": { "Authority": "file", "ClientId": "file" }, "App": { "PublicUrl": "file" }, "Urls": "file", "ConnectionStrings": { "MongoDB": "file" } }""");
		Environment.SetEnvironmentVariable($"{EnvironmentPrefix}Oidc__Authority", "environment");
		var configuration = new ConfigurationManager();
		configuration.AddJsonFile(appsettings);
		configuration.AddEnvironmentVariables(EnvironmentPrefix);
		configuration.AddCommandLine(["--App:PublicUrl=command-line"]);

		ProductionHosting.AddSettingsFile(configuration, production);

		configuration["ConnectionStrings:MongoDB"].ShouldBe("file");
		configuration["Oidc:ClientId"].ShouldBe("file");
		configuration["Urls"].ShouldBe("file");
		configuration["Oidc:Authority"].ShouldBe("environment");
		configuration["App:PublicUrl"].ShouldBe("command-line");
	}

	private string WriteJson(string name, string content)
	{
		var path = Path.Combine(_directory, name);
		File.WriteAllText(path, content);
		return path;
	}
}
