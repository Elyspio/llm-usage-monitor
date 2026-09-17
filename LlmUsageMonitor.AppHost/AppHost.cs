using System.Net.NetworkInformation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

builder.Services.AddLogging(logging => logging.AddSimpleConsole(options => options.SingleLine = true));

var reservedPorts = IPGlobalProperties.GetIPGlobalProperties()
	.GetActiveTcpListeners()
	.Select(endpoint => endpoint.Port)
	.ToHashSet();

static int SelectAvailablePort(int minimum, int maximum, ISet<int> reservedPorts)
{
	var count = maximum - minimum + 1;
	var start = Random.Shared.Next(count);

	for (var offset = 0; offset < count; offset++)
	{
		var candidate = minimum + (start + offset) % count;
		if (reservedPorts.Add(candidate))
		{
			return candidate;
		}
	}

	throw new InvalidOperationException($"No available TCP port found between {minimum} and {maximum}.");
}

// Keycloak redirect URIs are exact, so the front keeps a fixed origin (https://localhost:3000).
const int frontPort = 3000;
var keycloakPort = SelectAvailablePort(8000, 8999, reservedPorts);
var apiPort = SelectAvailablePort(7000, 7999, reservedPorts);

var mongo = builder.AddMongoDB("mongo")
	.WithDataVolume("llm-usage-monitor-mongo");
var mongoDatabase = mongo.AddDatabase("llm-usage-monitor");

// Development identity provider. No data volume: the realm is imported again on every run. Admin console: admin/admin.
var keycloakAdminUsername = builder.AddParameter("keycloak-admin-username", "admin", secret: false, publishValueAsDefault: true);
var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password", "admin", secret: false, publishValueAsDefault: true);
var keycloak = builder.AddKeycloak("keycloak", keycloakPort, keycloakAdminUsername, keycloakAdminPassword)
	.WithRealmImport("./Realms");

var oidcAuthority = ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/llm-usage-monitor");
const string oidcClientId = "i-llm-usage-monitor";

var api = builder.AddProject<LlmUsageMonitor_WebApi>("api")
	.WithEndpoint("https", annotation => annotation.Port = apiPort)
	.WithReference(mongoDatabase, "MongoDB")
	.WaitFor(mongoDatabase)
	.WaitFor(keycloak)
	.WithEnvironment("Oidc__Authority", oidcAuthority)
	.WithEnvironment("Oidc__ClientId", oidcClientId);

builder.AddViteApp("front", "../LlmUsageMonitor.Front")
	.WithPnpm()
	.WithEndpoint("http", annotation =>
	{
		annotation.Port = frontPort;
		annotation.TargetPort = frontPort;
		annotation.UriScheme = "https";
		annotation.IsProxied = false;
	})
	.WithReference(api)
	.WithEnvironment("VITE_OIDC_AUTHORITY", oidcAuthority)
	.WithEnvironment("VITE_OIDC_CLIENT_ID", oidcClientId)
	.WaitForStart(api);

builder.Build().Run();