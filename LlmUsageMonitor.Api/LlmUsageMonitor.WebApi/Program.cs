using System.Text.Json;
using System.Text.Json.Serialization;
using Elyspio.Utils.Telemetry.Technical.Extensions;
using Elyspio.Utils.Telemetry.Tracing.Builder;
using Hangfire;
using LlmUsageMonitor.Abstractions.Configurations;
using LlmUsageMonitor.Abstractions.Extensions;
using LlmUsageMonitor.Abstractions.Helpers;
using LlmUsageMonitor.Adapters.Claude;
using LlmUsageMonitor.Adapters.Codex;
using LlmUsageMonitor.Adapters.Hangfire;
using LlmUsageMonitor.Adapters.LiteLlm;
using LlmUsageMonitor.Adapters.MongoDB;
using LlmUsageMonitor.Adapters.Ntfy;
using LlmUsageMonitor.Authorization;
using LlmUsageMonitor.Core;
using LlmUsageMonitor.Filters;
using LlmUsageMonitor.Hosting;
using LlmUsageMonitor.OpenApi;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
builder.AddProductionHosting();

builder.Logging.AddSimpleConsole(x => x.SingleLine = true);

var telemetryEnabled = builder.Configuration.IsTelemetryEnabled(out var telemetryOptions);
if (telemetryEnabled)
{
	new AppOpenTelemetryBuilder<Program>(telemetryOptions!, builder.Configuration)
	{
		Tracing = (tracing, _) => tracing.AddHangfireInstrumentation()
	}.Build(builder.Services);
	builder.Services.AddOpenTelemetryJsonConfiguration(builder.Configuration);
}

builder.AddModule<CoreModule>();
builder.AddModule<MongoAdapterModule>();
builder.AddModule<ClaudeAdapterModule>();
builder.AddModule<CodexAdapterModule>();
builder.AddModule<NtfyAdapterModule>();
builder.AddModule<LiteLlmAdapterModule>();
builder.AddModule<HangfireAdapterModule>();

// Enums travel as camelCase strings, both in responses and in the OpenAPI document.
var enumConverter = new JsonStringEnumConverter(JsonNamingPolicy.CamelCase);
builder.Services.AddControllers(options =>
	{
		options.Filters.Add(new ProducesAttribute("application/json"));
		options.Filters.Add<HttpExceptionFilter>();
	})
	.AddJsonOptions(options =>
	{
		options.JsonSerializerOptions.Converters.Add(enumConverter);
		options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
	});
// Strict numbers: otherwise the OpenAPI document types every number as "integer | string".
builder.Services.ConfigureHttpJsonOptions(options =>
{
	options.SerializerOptions.Converters.Add(enumConverter);
	options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<OAuthSecurityTransformer>());

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
	.AddKeycloakBearer()
	.AddHangfireDashboardSignIn();

// Every endpoint requires the admin role unless it opts out: the anonymous ones are listed with AllowAnonymous.
builder.Services.AddAuthorizationBuilder()
	.AddAdminPolicy()
	.SetFallbackPolicy(AdminPolicy.Policy)
	.AddHangfireDashboardPolicy();

var app = builder.Build();

app.UseForwardedHeaders();

if (telemetryEnabled)
{
	app.UseOpenTelemetryJsonConfiguration();
}

// "/" serves index.html; the other client routes go through the SPA fallback (ProductionHosting.MapSpa).
// Static files are served before the authorization: the published SPA and its assets are public.
app.UseDefaultFiles();
app.UseStaticFiles();

// Swagger UI and the OpenAPI document outside production only: the document is committed with the front, and every call
// made from Swagger UI still requires the admin role. Before the routing, so the fallback policy never applies to the UI.
var exposeOpenApi = !app.Environment.IsProduction();
if (exposeOpenApi)
{
	app.UseSwaggerUI(options =>
	{
		options.SwaggerEndpoint("/openapi/v1.json", "LLM Usage Monitor");
		options.OAuthClientId(app.Services.GetRequiredService<IOptions<OidcConfig>>().Value.ClientId);
		options.OAuthUsePkce();
		options.OAuthScopes("openid");
	});
}

// Explicit, and after the static files: routing is otherwise inserted at the top of the pipeline, the SPA fallback matches
// every asset path, and the static file middleware steps aside for the endpoint already selected.
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

if (exposeOpenApi)
{
	app.MapOpenApi().AllowAnonymous();
}

app.MapControllers();

// Probes for the supervision (Uptime Kuma) and the AppHost: anonymous, the body is the status alone.
app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains(HealthCheckTags.Ready) }).AllowAnonymous();

if (HangfireAdapterModule.IsEnabled(app.Configuration))
	// Signed in with the cookie + OIDC scheme: the SPA bearer token does not follow the dashboard navigation.
{
	app.MapHangfireDashboard("/hangfire", new() { Authorization = [], DashboardTitle = "LLM Usage Monitor · jobs", AppPath = "/" })
		.RequireAuthorization(HangfireDashboardAuthentication.PolicyName);
}

app.MapSpa();

app.Run();

public partial class Program;