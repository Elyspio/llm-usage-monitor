using System.Net;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.WebApi.Tests;

public sealed class HostingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
	[Fact]
	public async Task The_runtime_configuration_is_public_and_carries_the_configured_realm()
	{
		using var client = factory.CreateClient();

		var response = await client.GetAsync("/conf.js", TestContext.Current.CancellationToken);

		response.StatusCode.ShouldBe(HttpStatusCode.OK);
		response.Content.Headers.ContentType!.MediaType.ShouldBe("text/javascript");
		var script = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		script.ShouldContain($"authority: \"{ApiFactory.Issuer}\"");
		script.ShouldContain($"clientId: \"{ApiFactory.ClientId}\"");
		script.ShouldContain("window.location.origin");
	}
}
