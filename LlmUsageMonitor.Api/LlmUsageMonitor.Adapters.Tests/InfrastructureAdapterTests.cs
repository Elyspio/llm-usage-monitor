using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using Cronos;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using LlmUsageMonitor.Adapters.Hangfire;
using LlmUsageMonitor.Adapters.MongoDB;
using LlmUsageMonitor.Adapters.Ntfy;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.Adapters.Tests;

public sealed class NtfySenderTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task A_message_is_published_as_json_with_the_token_of_its_server()
	{
		var handler = new StubHandler(HttpStatusCode.OK, "{}");
		var message = new NotificationMessage("Claude : reset détecté", "five_hour : 80 % → 0 %", NotificationPriority.High, ["arrows_counterclockwise"], "https://monitor.test");

		await new NtfySender(new(handler)).Send(message, "https://ntfy.test/", "llm", "tk_secret", Token);

		var request = handler.Requests.ShouldHaveSingleItem();
		request.Method.ShouldBe(HttpMethod.Post);
		request.RequestUri.ShouldBe(new Uri("https://ntfy.test"));
		request.Headers.Authorization!.ToString().ShouldBe("Bearer tk_secret");
		var body = JsonDocument.Parse(handler.Bodies.ShouldHaveSingleItem()).RootElement;
		body.GetProperty("topic").GetString().ShouldBe("llm");
		body.GetProperty("title").GetString().ShouldBe("Claude : reset détecté");
		body.GetProperty("message").GetString().ShouldBe("five_hour : 80 % → 0 %");
		body.GetProperty("priority").GetInt32().ShouldBe(4);
		body.GetProperty("tags")[0].GetString().ShouldBe("arrows_counterclockwise");
		body.GetProperty("click").GetString().ShouldBe("https://monitor.test");
	}

	[Fact]
	public async Task Without_token_no_authorization_is_sent()
	{
		var handler = new StubHandler(HttpStatusCode.OK, "{}");

		await new NtfySender(new(handler)).Send(new("t", "b", NotificationPriority.Default, [], null), "https://ntfy.test", "llm", null, Token);

		handler.Requests.ShouldHaveSingleItem().Headers.Authorization.ShouldBeNull();
		JsonDocument.Parse(handler.Bodies.Single()).RootElement.GetProperty("priority").GetInt32().ShouldBe(3);
	}

	[Fact]
	public async Task A_refused_message_is_a_delivery_failure_with_its_status()
	{
		var sender = new NtfySender(new(new StubHandler(HttpStatusCode.Forbidden, "{}")));

		var exception = await Should.ThrowAsync<HttpRequestException>(() => sender.Send(new("t", "b", NotificationPriority.Default, [], null), "https://ntfy.test", "llm", null, Token));

		exception.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
	}
}

public sealed class HangfireSchedulerTests
{
	[Theory]
	[InlineData(1, "*/1 * * * *")]
	[InlineData(3, "*/3 * * * *")]
	[InlineData(15, "*/15 * * * *")]
	[InlineData(30, "*/30 * * * *")]
	[InlineData(60, "0 * * * *")]
	// Saved before intervals had to divide the hour: rounded down to a divisor.
	[InlineData(7, "*/6 * * * *")]
	[InlineData(45, "*/30 * * * *")]
	[InlineData(90, "0 * * * *")]
	public void The_poll_cron_follows_the_interval_rounded_to_a_divisor_of_the_hour(int minutes, string cron)
	{
		HangfireJobScheduler.ToCron(minutes).ShouldBe(cron);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(4)]
	[InlineData(7)]
	[InlineData(25)]
	[InlineData(45)]
	[InlineData(59)]
	public void Every_interval_gives_evenly_spaced_polls_across_the_hours(int minutes)
	{
		var start = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);
		var occurrences = CronExpression.Parse(HangfireJobScheduler.ToCron(minutes)).GetOccurrences(start, start.AddDays(1), TimeZoneInfo.Utc).ToList();

		// "*/N" restarts at each hour: with N not dividing 60, the last gap of every hour would be shorter.
		occurrences.Zip(occurrences.Skip(1), (previous, next) => next - previous).Distinct().ShouldHaveSingleItem().TotalMinutes.ShouldBeLessThanOrEqualTo((double)minutes);
	}
}

public sealed class MongoDataProtectionTests(MongoFixture mongo) : IClassFixture<MongoFixture>
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void The_key_ring_is_stored_and_read_back()
	{
		var database = new MongoClient(mongo.CreateDatabaseUrl()).GetDatabase($"tests-{Guid.NewGuid():N}");
		var repository = new MongoXmlRepository(database);

		repository.StoreElement(new XElement("key", new XAttribute("id", "1")), "key-1");
		repository.StoreElement(new XElement("key", new XAttribute("id", "2")), "key-2");

		repository.GetAllElements().Select(element => element.Attribute("id")!.Value).ShouldBe(["1", "2"]);
	}

	[Fact]
	public async Task A_secret_protected_by_one_process_is_read_by_the_next_one()
	{
		var url = mongo.CreateDatabaseUrl();
		string protectedToken;
		await using (var first = await mongo.CreateServices(url))
		{
			protectedToken = first.GetRequiredService<ISecretProtector>().Protect("tk_secret");
		}

		await using var next = await mongo.CreateServices(url);

		protectedToken.ShouldNotContain("tk_secret");
		next.GetRequiredService<ISecretProtector>().Unprotect(protectedToken).ShouldBe("tk_secret");
		var keys = await next.GetRequiredService<IMongoDatabase>().GetCollection<BsonDocument>(Collections.DataProtectionKeys).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: Token);
		keys.ShouldBe(1);
	}
}
