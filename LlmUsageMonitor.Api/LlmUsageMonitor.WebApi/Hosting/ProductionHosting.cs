using System.Net;
using System.Text.Json;
using LlmUsageMonitor.Abstractions.Configurations;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace LlmUsageMonitor.Hosting;

/// <summary>
///     Hosting on the LXC: settings outside the deployed directory, HAProxy in front, SPA served by the API.
/// </summary>
public static class ProductionHosting
{
	/// <summary>Environment variable naming the production settings file (outside the directory each deployment overwrites).</summary>
	public const string SettingsFileVariable = "LLM_USAGE_MONITOR_SETTINGS";

	private const string ApiPaths = "api/|hangfire|swagger|openapi|signin-oidc|conf\\.js";

	public static WebApplicationBuilder AddProductionHosting(this WebApplicationBuilder builder)
	{
		if (Environment.GetEnvironmentVariable(SettingsFileVariable) is { Length: > 0 } settingsFile)
		{
			builder.Configuration.AddJsonFile(settingsFile, optional: false, reloadOnChange: true);
		}

		// HAProxy terminates TLS: only the declared proxies may set the scheme and client address (OIDC redirects must stay https).
		var knownProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
		builder.Services.Configure<ForwardedHeadersOptions>(options =>
		{
			options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
			foreach (var proxy in knownProxies)
			{
				options.KnownProxies.Add(IPAddress.TryParse(proxy, out var address)
					? address
					: throw new InvalidOperationException($"ForwardedHeaders:KnownProxies contains an invalid IP address: '{proxy}'."));
			}
		});

		return builder;
	}

	/// <summary>
	///     Serves the built SPA from <c>wwwroot</c> when it is deployed, with the runtime <c>/conf.js</c> and a fallback to
	///     <c>index.html</c> for the client routes.
	/// </summary>
	public static void MapSpa(this WebApplication app)
	{
		app.MapGet("/conf.js", (IOptions<OidcConfig> oidc) => Results.Text(RuntimeConfigScript(oidc.Value), "text/javascript; charset=utf-8"))
			.ExcludeFromDescription();

		if (app.Environment.WebRootPath is { } webRoot && File.Exists(Path.Combine(webRoot, "index.html")))
		{
			app.MapFallbackToFile($"{{*path:regex(^(?!{ApiPaths}).*$)}}", "index.html");
		}
	}

	/// <summary>
	///     The configuration read by the SPA before it starts; the origin comes from the browser.
	/// </summary>
	public static string RuntimeConfigScript(OidcConfig oidc)
	{
		var authority = JsonSerializer.Serialize(oidc.Authority);
		var clientId = JsonSerializer.Serialize(oidc.ClientId);
		return $$"""
			window["llm-usage-monitor"] = {
				config: {
					endpoints: { apiUrl: window.location.origin },
					oauth: { authority: {{authority}}, clientId: {{clientId}}, callbackUrl: `${window.location.origin}/auth/callback` },
				},
			};
			""";
	}
}
