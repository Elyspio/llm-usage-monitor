using System.Text.Json;
using System.Text.Json.Serialization;
using Elyspio.Utils.Telemetry.Technical.Extensions;
using Elyspio.Utils.Telemetry.Tracing.Builder;
using LlmUsageMonitor.Abstractions.Configurations;
using LlmUsageMonitor.Abstractions.Extensions;
using LlmUsageMonitor.Adapters.Claude;
using LlmUsageMonitor.Adapters.Codex;
using LlmUsageMonitor.Adapters.MongoDB;
using LlmUsageMonitor.Authorization;
using LlmUsageMonitor.Core;
using LlmUsageMonitor.OpenApi;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var telemetryEnabled = builder.Configuration.IsTelemetryEnabled(out var telemetryOptions);
if (telemetryEnabled)
{
	new AppOpenTelemetryBuilder<Program>(telemetryOptions!, builder.Configuration).Build(builder.Services);
	builder.Services.AddOpenTelemetryJsonConfiguration(builder.Configuration);
}

builder.AddModule<CoreModule>();
builder.AddModule<MongoAdapterModule>();
builder.AddModule<ClaudeAdapterModule>();
builder.AddModule<CodexAdapterModule>();

// Enums travel as camelCase strings, both in responses and in the OpenAPI document.
var enumConverter = new JsonStringEnumConverter(JsonNamingPolicy.CamelCase);
builder.Services.AddControllers(options => options.Filters.Add(new ProducesAttribute("application/json")))
	.AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(enumConverter));
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(enumConverter));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<OAuthSecurityTransformer>());

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
	.Configure<IOptions<OidcConfig>, IHostEnvironment>((options, oidc, environment) =>
	{
		options.Authority = oidc.Value.Authority;
		// The development Keycloak started by Aspire only listens on HTTP.
		options.RequireHttpsMetadata = !environment.IsDevelopment();
		options.TokenValidationParameters = new TokenValidationParameters
		{
			ValidIssuer = oidc.Value.Authority,
			ValidAudience = oidc.Value.ClientId,
			ClockSkew = TimeSpan.FromSeconds(30),
		};
	});
builder.Services.AddAuthorizationBuilder().AddAdminPolicy();

var app = builder.Build();

if (telemetryEnabled)
{
	app.UseOpenTelemetryJsonConfiguration();
}

app.UseAuthentication();
app.UseAuthorization();

// The document and Swagger UI are public; every call made from it still requires the admin role.
app.MapOpenApi();
app.UseSwaggerUI(options =>
{
	options.SwaggerEndpoint("/openapi/v1.json", "LLM Usage Monitor");
	options.OAuthClientId(app.Services.GetRequiredService<IOptions<OidcConfig>>().Value.ClientId);
	options.OAuthUsePkce();
	options.OAuthScopes("openid");
});

app.MapControllers();

app.Run();

public partial class Program;
