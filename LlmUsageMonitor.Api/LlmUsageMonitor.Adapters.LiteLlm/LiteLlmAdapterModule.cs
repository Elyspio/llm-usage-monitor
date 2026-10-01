using System.Text.Json;
using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Injections;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LlmUsageMonitor.Adapters.LiteLlm;

public sealed class LiteLlmAdapterModule : IModule
{
	/// <summary>The public price table maintained by LiteLLM, also used by ccusage.</summary>
	public const string DefaultPricesUrl = "https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json";

	public void Load(IServiceCollection services, IConfiguration configuration)
	{
		var url = configuration.GetValue("LiteLlm:PricesUrl", DefaultPricesUrl)!;
		services.AddHttpClient<IModelPriceSource, LiteLlmPriceSource>(client =>
		{
			client.BaseAddress = new(url);
			client.Timeout = TimeSpan.FromSeconds(60);
		});
	}
}

/// <summary>
///     Downloads the LiteLLM table and keeps the Anthropic and OpenAI chat models, the only ones the CLIs use.
/// </summary>
internal sealed class LiteLlmPriceSource(HttpClient http) : IModelPriceSource
{
	private static readonly HashSet<string> Providers = ["anthropic", "openai"];

	public async Task<IReadOnlyList<ModelPrice>> Fetch(CancellationToken cancellationToken)
	{
		await using var stream = await http.GetStreamAsync((Uri?)null, cancellationToken);
		using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
		return Parse(document.RootElement);
	}

	internal static IReadOnlyList<ModelPrice> Parse(JsonElement table)
	{
		var prices = new List<ModelPrice>();
		foreach (var entry in table.EnumerateObject())
		{
			var model = entry.Value;
			if (model.ValueKind != JsonValueKind.Object
			    || !model.TryGetProperty("litellm_provider", out var provider)
			    || provider.ValueKind != JsonValueKind.String
			    || !Providers.Contains(provider.GetString()!)
			    || Number(model, "input_cost_per_token") is not { } input
			    || Number(model, "output_cost_per_token") is not { } output)
			{
				continue;
			}

			prices.Add(new(entry.Name, input, output, Number(model, "cache_read_input_token_cost"), Number(model, "cache_creation_input_token_cost")));
		}

		return prices;
	}

	private static double? Number(JsonElement model, string property)
	{
		return model.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
	}
}
