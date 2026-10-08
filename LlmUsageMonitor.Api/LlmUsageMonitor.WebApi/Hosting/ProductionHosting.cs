using System.Net;
using System.Reflection;
using System.Text.Json;
using LlmUsageMonitor.Abstractions.Configurations;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
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

	/// <summary>Directory of the embedded files holding the SPA, in a release build.</summary>
	private const string EmbeddedSpaDirectory = "wwwroot";

	/// <summary>The version of the build (<c>0.0.0-dev</c> outside a release), shown by the SPA.</summary>
	public static string Version { get; } =
		typeof(ProductionHosting).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0-dev";

	public static WebApplicationBuilder AddProductionHosting(this WebApplicationBuilder builder)
	{
		AddDefaultSettings(builder.Configuration, new ManifestEmbeddedFileProvider(typeof(ProductionHosting).Assembly));
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
	///     Adds the <c>appsettings.json</c> of <paramref name="files" /> below every other source: the executable carries its
	///     defaults, and an <c>appsettings</c> file on disk, the production settings file, an environment variable or a
	///     command-line argument overrides them.
	/// </summary>
	public static void AddDefaultSettings(IConfigurationBuilder configuration, IFileProvider files)
	{
		configuration.AddJsonFile(files, "appsettings.json", true, false);

		var sources = configuration.Sources;
		var defaults = sources[^1];
		sources.RemoveAt(sources.Count - 1);
		var firstJsonFile = 0;
		while (firstJsonFile < sources.Count && sources[firstJsonFile] is not JsonConfigurationSource)
			firstJsonFile++;

		sources.Insert(firstJsonFile == sources.Count ? 0 : firstJsonFile, defaults);
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
	///     The SPA embedded in <paramref name="assembly" /> by a release build, or <c>null</c> when it has none.
	/// </summary>
	public static IFileProvider? EmbeddedSpa(Assembly assembly)
	{
		var files = new ManifestEmbeddedFileProvider(assembly);
		return files.GetFileInfo($"{EmbeddedSpaDirectory}/index.html").Exists ? new ManifestEmbeddedFileProvider(assembly, EmbeddedSpaDirectory) : null;
	}

	/// <summary>
	///     A release build serves the SPA embedded in the executable, whatever a <c>wwwroot</c> directory on disk holds; any
	///     other build serves <c>wwwroot</c>. Called before the static files middleware, which reads the provider.
	/// </summary>
	public static void UseEmbeddedSpa(this WebApplication app)
	{
		if (EmbeddedSpa(typeof(ProductionHosting).Assembly) is { } spa)
		{
			app.Environment.WebRootFileProvider = spa;
		}
	}

	/// <summary>
	///     Serves the built SPA (<see cref="UseEmbeddedSpa" />) when there is one, with the runtime <c>/conf.js</c> and a
	///     fallback to <c>index.html</c> for the client routes.
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

		if (app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists)
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
		var version = JsonSerializer.Serialize(Version);
		return $$"""
		         window["llm-usage-monitor"] = {
		         	config: {
		         		version: {{version}},
		         		endpoints: { apiUrl: window.location.origin },
		         		oauth: { authority: {{authority}}, clientId: {{clientId}}, callbackUrl: `${window.location.origin}/auth/callback` },
		         	},
		         };
		         """;
	}
}