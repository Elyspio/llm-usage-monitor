using System.Net.Http.Headers;
using System.Security.Cryptography;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

namespace LlmUsageMonitor.WebApi.Tests;

/// <summary>
///     Hosts the API on a real MongoDB, without Hangfire (jobs are recorded, never run) and with a local signing key in place
///     of Keycloak, so tests issue their own access tokens.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
	public const string Issuer = "https://auth.test/realms/llm-usage-monitor";
	public const string ClientId = "i-llm-usage-monitor";
	public const string AdminRole = "llm-usage-monitor:admin";

	private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "test" };

	private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:8.0").Build();

	public RecordingScheduler Scheduler { get; } = new();

	public async ValueTask InitializeAsync() => await _mongo.StartAsync();

	public override async ValueTask DisposeAsync()
	{
		await base.DisposeAsync();
		await _mongo.DisposeAsync();
	}

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		var connectionString = new MongoUrlBuilder(_mongo.GetConnectionString()) { DatabaseName = "llm-usage-monitor", AuthenticationSource = "admin" }.ToString();

		builder.UseEnvironment("Testing");
		builder.UseSetting("ConnectionStrings:MongoDB", connectionString);
		builder.UseSetting("Hangfire:Enabled", "false");
		builder.UseSetting("Oidc:Authority", Issuer);
		builder.UseSetting("Oidc:ClientId", ClientId);
		builder.ConfigureTestServices(services =>
		{
			services.RemoveAll<IJobScheduler>();
			services.AddSingleton<IJobScheduler>(Scheduler);
			services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
			{
				// Set before the JwtBearer post-configuration, a static configuration keeps the handler from fetching Keycloak metadata.
				options.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
				options.TokenValidationParameters.IssuerSigningKey = SigningKey;
			});
		});
	}

	/// <summary>
	///     Creates a client authenticated with an access token carrying the given client roles.
	/// </summary>
	public HttpClient CreateClientWithRoles(params string[] clientRoles)
	{
		var client = CreateClient();
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(clientRoles));
		return client;
	}

	private static string CreateToken(string[] clientRoles) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
	{
		Issuer = Issuer,
		Audience = ClientId,
		Expires = DateTime.UtcNow.AddMinutes(5),
		Claims = new Dictionary<string, object>
		{
			["sub"] = "test-user",
			["resource_access"] = new Dictionary<string, object> { [ClientId] = new Dictionary<string, object> { ["roles"] = clientRoles } },
		},
		SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
	});
}

public sealed class RecordingScheduler : IJobScheduler
{
	private int _nextId;

	public List<string> EnqueuedTriggers { get; } = [];

	public void SetPollInterval(Provider provider, int minutes)
	{
	}

	public void EnqueuePoll(Provider provider)
	{
	}

	public string SchedulePostResetCheck(Provider provider, DateTimeOffset runAt) => $"job-{Interlocked.Increment(ref _nextId)}";

	public string ScheduleKeepAlive(DateTimeOffset runAt) => $"job-{Interlocked.Increment(ref _nextId)}";

	public void EnqueueTrigger(string runId)
	{
		lock (EnqueuedTriggers) EnqueuedTriggers.Add(runId);
	}

	public void Delete(string jobId)
	{
	}
}
