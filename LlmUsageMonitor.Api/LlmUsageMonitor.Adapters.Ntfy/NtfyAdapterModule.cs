using System.Net.Http.Headers;
using System.Net.Http.Json;
using LlmUsageMonitor.Abstractions.Injections;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LlmUsageMonitor.Adapters.Ntfy;

public sealed class NtfyAdapterModule : IModule
{
	public void Load(IServiceCollection services, IConfiguration configuration)
	{
		services.AddHttpClient<INotificationSender, NtfySender>(client => client.Timeout = TimeSpan.FromSeconds(15));
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
				click = message.ClickUrl,
			}),
		};
		if (!string.IsNullOrEmpty(token))
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		}

		using var response = await http.SendAsync(request, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"ntfy returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
		}
	}
}
