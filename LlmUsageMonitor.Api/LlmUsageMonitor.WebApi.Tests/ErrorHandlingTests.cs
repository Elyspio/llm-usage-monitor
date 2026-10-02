using System.Diagnostics;
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
	public async Task A_mapped_exception_is_recorded_as_an_event_of_the_request_activity()
	{
		var runId = Guid.NewGuid().ToString("N");
		// The server stops the request activity once the response is sent: it may end just after the client got the response.
		var stopped = new TaskCompletionSource<Activity>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var listener = new ActivityListener
		{
			ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
			ActivityStopped = activity =>
			{
				if (activity.Events.Any(e => Tag(e, "exception.message")?.Contains(runId) == true))
				{
					stopped.TrySetResult(activity);
				}
			}
		};
		ActivitySource.AddActivityListener(listener);
		using var client = factory.CreateClientWithRoles(ApiFactory.AdminRole);

		var response = await client.GetAsync($"/api/trigger-runs/{runId}", Token);

		response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
		var activity = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
		var exception = activity.Events.Single(e => e.Name == "exception");
		Tag(exception, "exception.type").ShouldBe("LlmUsageMonitor.Abstractions.Exceptions.ResourceNotFoundException");
		activity.GetTagItem("exception").ShouldBeNull();
	}

	[Fact]
	public void A_service_behind_several_interfaces_has_a_single_instance()
	{
		var triggers = factory.Services.GetRequiredService<ITriggerService>();

		factory.Services.GetRequiredService<IAutomaticTrigger>().ShouldBeSameAs(triggers);
	}

	private static string? Tag(ActivityEvent activityEvent, string name)
	{
		return activityEvent.Tags.FirstOrDefault(tag => tag.Key == name).Value?.ToString();
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
