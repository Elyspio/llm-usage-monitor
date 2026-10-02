using System.Net;
using System.Text.Json;
using LlmUsageMonitor.Abstractions.Configurations;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Options;

namespace LlmUsageMonitor.Hosting;

/// <summary>
///     Hosting on the LXC: settings outside the deployed directory, HAProxy in front, SPA served by the API.
/// </summary>
public static class ProductionHosting
{
	/// <summary>Environment variable naming the production settings file (outside the directory each deployment overwrites).</summary>
	public const string SettingsFileVariable = "LLM_USAGE_MONITOR_SETTINGS";

	private const string ApiPaths = "api/|health/|hangfire|swagger|openapi|signin-oidc|conf\\.js";

	public static WebApplicationBuilder AddProductionHosting(this WebApplicationBuilder builder)
	{
		if (Environment.GetEnvironmentVariable(SettingsFileVariable) is { Length: > 0 } settingsFile)
		{
			AddSettingsFile(builder.Configuration, settingsFile);
		}

		// HAProxy terminates TLS: only the declared proxies may set the scheme and client address (OIDC redirects must stay https).
		var knownProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
		builder.Services.Configure<ForwardedHeadersOptions>(options =>
		{
			options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
			foreach (var proxy in knownProxies)
				options.KnownProxies.Add(IPAddress.TryParse(proxy, out var address)
					? address
					: throw new InvalidOperationException($"ForwardedHeaders:KnownProxies contains an invalid IP address: '{proxy}'."));
		});

		return builder;
	}

	/// <summary>
	///     Adds the production settings file right after the <c>appsettings</c> files: it overrides them, while an environment
	///     variable (unprefixed, e.g. <c>Oidc__Authority</c>) or a command-line argument still overrides the file.
	/// </summary>
	public static void AddSettingsFile(IConfigurationBuilder configuration, string settingsFile)
	{
		configuration.AddJsonFile(settingsFile, false, true);

		var sources = configuration.Sources;
		var settings = sources[^1];
		sources.RemoveAt(sources.Count - 1);
		var lastJsonFile = -1;
		for (var index = 0; index < sources.Count; index++)
			if (sources[index] is JsonConfigurationSource)
			{
				lastJsonFile = index;
			}

		sources.Insert(lastJsonFile + 1, settings);
	}

	/// <summary>
	///     In production, every response but the Hangfire dashboard (its own pages, its own sign-in) carries the
	///     <see cref="ContentSecurityPolicy" />. Placed before the static files, which serve the SPA.
	/// </summary>
	public static void UseContentSecurityPolicy(this WebApplication app)
	{
		if (!app.Environment.IsProduction())
		{
			return;
		}

		var policy = ContentSecurityPolicy(app.Services.GetRequiredService<IOptions<OidcConfig>>().Value);
		app.Use((context, next) =>
		{
			if (!context.Request.Path.StartsWithSegments("/hangfire"))
			{
				context.Response.Headers.ContentSecurityPolicy = policy;
			}

			return next(context);
		});
	}

	/// <summary>
	///     The scripts come from the application only (index.html has no inline script, <c>/conf.js</c> is a file), the API
	///     calls go to the application and to Keycloak (metadata, token, refresh).
	/// </summary>
	public static string ContentSecurityPolicy(OidcConfig oidc)
	{
		var keycloak = new Uri(oidc.Authority).GetLeftPart(UriPartial.Authority);
		return string.Join("; ",
			"default-src 'self'",
			"script-src 'self'",
			// Inline styles, never scripts: Emotion (the styling engine of MUI) inserts its <style> elements at runtime, and MUI
			// sets style attributes. A nonce would need index.html rendered per request and the nonce given to Emotion.
			"style-src 'self' 'unsafe-inline'",
			// Vite inlines the small assets (fonts, images) as data: URLs.
			"img-src 'self' data:",
			"font-src 'self' data:",
			$"connect-src 'self' {keycloak}",
			"object-src 'none'",
			"base-uri 'self'",
			"form-action 'self'",
			"frame-ancestors 'none'");
	}

	/// <summary>
	///     Serves the built SPA from <c>wwwroot</c> when it is deployed, with the runtime <c>/conf.js</c> and a fallback to
	///     <c>index.html</c> for the client routes.
	/// </summary>
	public static void MapSpa(this WebApplication app)
	{
		// Read before the sign-in: public, like the SPA itself. Never cached: a configuration change applies on the next load.
		app.MapGet("/conf.js", (HttpContext context, IOptions<OidcConfig> oidc) =>
			{
				context.Response.Headers.CacheControl = "no-store";
				return Results.Text(RuntimeConfigScript(oidc.Value), "text/javascript; charset=utf-8");
			})
			.AllowAnonymous()
			.ExcludeFromDescription();

		if (app.Environment.WebRootPath is { } webRoot && File.Exists(Path.Combine(webRoot, "index.html")))
		{
			app.MapFallbackToFile($"{{*path:regex(^(?!{ApiPaths}).*$)}}", "index.html")
				.AllowAnonymous();
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