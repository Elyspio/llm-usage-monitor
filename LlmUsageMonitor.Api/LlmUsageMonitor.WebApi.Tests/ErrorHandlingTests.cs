using System.Net;
using System.Text.Json;
using LlmUsageMonitor.Abstractions.Interfaces.Services;
using LlmUsageMonitor.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.WebApi.Tests;

public sealed class ErrorHandlingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task An_unhandled_exception_becomes_a_500_problem_without_its_details()
	{
		using var client = factory.CreateClientWithRoles(ApiFactory.AdminRole);

		var response = await client.GetAsync("/api/tests/failure", Token);

		response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
		response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
		var body = await response.Content.ReadAsStringAsync(Token);
		JsonDocument.Parse(body).RootElement.GetProperty("status").GetInt32().ShouldBe(500);
		body.ShouldNotContain(FailingProbeController.Secret);
	}

	[Fact]
	public async Task A_business_error_becomes_a_problem_with_its_code()
	{
		using var client = factory.CreateClientWithRoles(ApiFactory.AdminRole);

		var response = await client.GetAsync($"/api/trigger-runs/{Guid.NewGuid():N}", Token);

		response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
		response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
		var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement;
		problem.GetProperty("type").GetString().ShouldBe("urn:problem:resource.not-found");
		problem.GetProperty("code").GetString().ShouldBe("resource.not-found");
		problem.TryGetProperty("traceId", out _).ShouldBeTrue();
	}

	[Fact]
	public void A_service_behind_several_interfaces_has_a_single_instance()
	{
		var triggers = factory.Services.GetRequiredService<ITriggerService>();

		factory.Services.GetRequiredService<IAutomaticTrigger>().ShouldBeSameAs(triggers);
	}
}

/// <summary>An endpoint that fails with an exception no filter maps, as a bug would.</summary>
[ApiController]
[Route("api/tests/failure")]
public sealed class FailingProbeController : ControllerBase
{
	public const string Secret = "internal detail that must not leak";

	[HttpGet]
	public string Get()
	{
		throw new InvalidOperationException(Secret);
	}
}
