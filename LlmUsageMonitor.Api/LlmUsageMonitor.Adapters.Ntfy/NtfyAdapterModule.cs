using System.Net.Http.Json;
using LlmUsageMonitor.Abstractions.Injections;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace LlmUsageMonitor.Adapters.Ntfy;

public sealed class NtfyAdapterModule : IModule
{
	public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(5);
	public static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(20);

	public void Load(IServiceCollection services, IConfiguration configuration)
	{
		// Retries, timeouts and circuit breaker: a timeout surfaces as a delivery failure (TimeoutRejectedException), never as
		// a cancellation of the caller. A duplicate notification is preferred to a lost one, so the POST is retried too.
		services.AddHttpClient<INotificationSender, NtfySender>()
			.AddStandardResilienceHandler(options =>
			{
				options.AttemptTimeout.Timeout = AttemptTimeout;
				options.TotalRequestTimeout.Timeout = TotalTimeout;
			});
	}
}

/// <summary>
///     Publishes to ntfy with its JSON API, which carries non-ASCII titles safely (headers would not).
/// </summary>
internal sealed class NtfySender(HttpClient http) : INotificationSender
{
	public async Task Send(NotificationMessage message, string serverUrl, string topic, string? token, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, serverUrl.TrimEnd('/'))
		{
			Content = JsonContent.Create(new
			{
				topic,
				title = message.Title,
				message = message.Body,
				priority = message.Priority == NotificationPriority.High ? 4 : 3,
				tags = message.Tags,
				click = message.ClickUrl
			})
		};
		if (!string.IsNullOrEmpty(token))
		{
			request.Headers.Authorization = new("Bearer", token);
		}

		using var response = await http.SendAsync(request, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"ntfy returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
		}
	}
}