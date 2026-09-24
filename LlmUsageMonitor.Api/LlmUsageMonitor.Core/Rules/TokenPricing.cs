using System.Text.RegularExpressions;
using LlmUsageMonitor.Abstractions.Data;

namespace LlmUsageMonitor.Core.Rules;

/// <summary>
///     The API-equivalent cost of the tokens of a model, from the LiteLLM price table.
/// </summary>
public static partial class TokenPricing
{
	/// <summary>
	///     The price table keys a model name may match, by order of preference: the exact name, then the name without its
	///     release date (<c>claude-haiku-4-5-20251001</c> → <c>claude-haiku-4-5</c>).
	/// </summary>
	public static IReadOnlyList<string> Candidates(string model)
	{
		var undated = DateSuffix().Replace(model, "");
		return undated == model ? [model] : [model, undated];
	}

	/// <summary>
	///     Returns the price of the first candidate of <paramref name="model" /> found in <paramref name="prices" />.
	/// </summary>
	public static ModelPrice? Resolve(string model, IReadOnlyDictionary<string, ModelPrice> prices)
	{
		return Candidates(model).Select(candidate => prices.GetValueOrDefault(candidate)).FirstOrDefault(price => price is { });
	}

	/// <summary>
	///     Cost at the flat per-token prices; the long context surcharges do not apply to hourly totals. The cache prices
	///     default to the input price when the table has none.
	/// </summary>
	public static TokenCost Cost(TokenCounts tokens, ModelPrice price)
	{
		var cacheRead = price.CacheReadPerToken ?? price.InputPerToken;
		var cacheWrite = price.CacheWritePerToken ?? price.InputPerToken;

		var usd = tokens.Input * price.InputPerToken
		          + tokens.CacheRead * cacheRead
		          + tokens.CacheWrite * cacheWrite
		          + tokens.Output * price.OutputPerToken;
		var savings = tokens.CacheRead * (price.InputPerToken - cacheRead);

		return new(usd, savings);
	}

	[GeneratedRegex(@"-\d{8}$")]
	private static partial Regex DateSuffix();
}
