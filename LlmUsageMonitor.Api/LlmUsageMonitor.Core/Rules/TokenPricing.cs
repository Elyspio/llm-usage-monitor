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
	///     release date (<c>claude-haiku-4-5-20251001</c> → <c>claude-haiku-4-5</c>), then the Claude name with its version
	///     dots written as dashes, as GitHub Copilot names them (<c>claude-sonnet-4.5</c> → <c>claude-sonnet-4-5</c>).
	/// </summary>
	public static IReadOnlyList<string> Candidates(string model)
	{
		var undated = DateSuffix().Replace(model, "");
		var dashed = model.StartsWith("claude-", StringComparison.Ordinal) ? undated.Replace('.', '-') : undated;
		return new[] { model, undated, dashed }.Distinct(StringComparer.Ordinal).ToList();
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

	/// <summary>
	///     Splits the stored cost of <paramref name="tokens" /> by token type, in proportion to what each type costs at
	///     <paramref name="price" />, so the split always adds up to the stored cost even after a price change. Without a
	///     price (the model left the table), the split follows the token counts.
	/// </summary>
	public static TokenCostByType Split(TokenCounts tokens, double usd, ModelPrice? price)
	{
		var weights = price is { }
			? new TokenCostByType(
				tokens.Input * price.InputPerToken,
				tokens.CacheRead * (price.CacheReadPerToken ?? price.InputPerToken),
				tokens.CacheWrite * (price.CacheWritePerToken ?? price.InputPerToken),
				tokens.Output * price.OutputPerToken)
			: new TokenCostByType(tokens.Input, tokens.CacheRead, tokens.CacheWrite, tokens.Output);

		var total = weights.Input + weights.CacheRead + weights.CacheWrite + weights.Output;
		if (total <= 0) return TokenCostByType.Zero;

		var scale = usd / total;
		return new(weights.Input * scale, weights.CacheRead * scale, weights.CacheWrite * scale, weights.Output * scale);
	}

	[GeneratedRegex(@"-\d{8}$")]
	private static partial Regex DateSuffix();
}
