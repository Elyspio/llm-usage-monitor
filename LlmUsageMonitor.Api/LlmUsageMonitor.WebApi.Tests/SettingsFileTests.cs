using LlmUsageMonitor.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
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

	[Fact]
	public void The_embedded_defaults_are_overridden_by_every_other_source()
	{
		var embedded = Directory.CreateDirectory(Path.Combine(_directory, "embedded")).FullName;
		File.WriteAllText(Path.Combine(embedded, "appsettings.json"),
			"""{ "Oidc": { "Authority": "embedded", "ClientId": "embedded" }, "App": { "PublicUrl": "embedded" }, "Urls": "embedded", "AllowedHosts": "embedded" }""");
		var appsettings = WriteJson("appsettings.json", """{ "Urls": "appsettings" }""");
		var production = WriteJson("appsettings.Production.json", """{ "Oidc": { "ClientId": "file" } }""");
		Environment.SetEnvironmentVariable($"{EnvironmentPrefix}Oidc__Authority", "environment");
		var configuration = new ConfigurationManager();
		configuration.AddEnvironmentVariables("DOTNET_");
		configuration.AddJsonFile(appsettings);
		configuration.AddEnvironmentVariables(EnvironmentPrefix);
		configuration.AddCommandLine(["--App:PublicUrl=command-line"]);

		using var files = new PhysicalFileProvider(embedded);
		ProductionHosting.AddDefaultSettings(configuration, files);
		ProductionHosting.AddSettingsFile(configuration, production);

		configuration["AllowedHosts"].ShouldBe("embedded");
		configuration["Urls"].ShouldBe("appsettings");
		configuration["Oidc:ClientId"].ShouldBe("file");
		configuration["Oidc:Authority"].ShouldBe("environment");
		configuration["App:PublicUrl"].ShouldBe("command-line");
	}

	[Fact]
	public void The_executable_carries_its_default_settings()
	{
		var configuration = new ConfigurationManager();

		ProductionHosting.AddDefaultSettings(configuration, new ManifestEmbeddedFileProvider(typeof(ProductionHosting).Assembly));

		configuration["Oidc:ClientId"].ShouldBe("i-llm-usage-monitor");
	}

	private string WriteJson(string name, string content)
	{
		var path = Path.Combine(_directory, name);
		File.WriteAllText(path, content);
		return path;
	}
}
