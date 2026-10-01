namespace LlmUsageMonitor.Abstractions.Data;

/// <summary>
///     Token counts of a bucket, as read from the session logs of a workstation.
/// </summary>
/// <param name="Input">The input tokens read without cache.</param>
/// <param name="CacheRead">The input tokens read from the prompt cache.</param>
/// <param name="CacheWrite">The input tokens written to the prompt cache.</param>
/// <param name="Output">The output tokens, reasoning included.</param>
public sealed record TokenCounts(long Input, long CacheRead, long CacheWrite, long Output)
{
	public static readonly TokenCounts Zero = new(0, 0, 0, 0);

	public TokenCounts Add(TokenCounts other)
	{
		return new(Input + other.Input, CacheRead + other.CacheRead, CacheWrite + other.CacheWrite, Output + other.Output);
	}
}

/// <summary>
///     The absolute token counts of one hour, provider and model on a workstation. A new upload replaces the stored value.
/// </summary>
/// <param name="Provider">The provider.</param>
/// <param name="Model">The model name, as written in the session logs.</param>
/// <param name="Hour">The start of the hour, UTC-aligned.</param>
/// <param name="Tokens">The totals of the hour.</param>
public sealed record TokenUsageBucketUpload(Provider Provider, string Model, DateTimeOffset Hour, TokenCounts Tokens);

/// <summary>
///     The hours changed on a workstation since its last upload.
/// </summary>
/// <param name="MachineId">The stable workstation identifier, generated once by the collector.</param>
/// <param name="MachineName">The workstation label shown in the filter; it can change without splitting the history.</param>
/// <param name="Buckets">At most <see cref="MaxBuckets" /> buckets; an empty list only records the upload.</param>
public sealed record TokenUsageUpload(string MachineId, string MachineName, IReadOnlyList<TokenUsageBucketUpload> Buckets)
{
	public const int MaxBuckets = 2000;
	public const int MaxIdLength = 64;
	public const int MaxModelLength = 128;
}

/// <param name="Stored">The buckets written.</param>
/// <param name="Unpriced">The buckets whose model has no known price.</param>
public sealed record TokenUsageUploadResult(int Stored, int Unpriced);

/// <summary>
///     A stored bucket, with the cost computed when it was uploaded.
/// </summary>
public sealed record TokenUsageBucket(string MachineId, Provider Provider, string Model, DateTimeOffset Hour, TokenCounts Tokens, TokenCost? Cost);

/// <summary>
///     The API-equivalent price of a bucket.
/// </summary>
/// <param name="Usd">The cost in US dollars.</param>
/// <param name="CacheSavingsUsd">What the cached input would have cost more at the input price.</param>
public sealed record TokenCost(double Usd, double CacheSavingsUsd);

/// <summary>
///     The per-token prices of a model, from the LiteLLM price table.
/// </summary>
/// <param name="Model">The model name.</param>
/// <param name="InputPerToken">The input price.</param>
/// <param name="OutputPerToken">The output price.</param>
/// <param name="CacheReadPerToken">The cache read price; the input price when unknown.</param>
/// <param name="CacheWritePerToken">The cache write price; the input price when unknown.</param>
public sealed record ModelPrice(string Model, double InputPerToken, double OutputPerToken, double? CacheReadPerToken, double? CacheWritePerToken);

/// <summary>
///     A workstation that uploads token usage.
/// </summary>
public sealed record UsageMachine(string Id, string Name, DateTimeOffset LastUploadAt);

public enum TokenUsageRange
{
	/// <summary>The last 24 hours, by hour.</summary>
	Last24Hours,

	/// <summary>Today and the 6 previous days, by local day.</summary>
	Last7Days,

	/// <summary>Today and the 29 previous days, by local day.</summary>
	Last30Days,

	/// <summary>Today and the 89 previous days, by local day.</summary>
	Last90Days,

	/// <summary>From the local day of the first stored hour to today, by local day.</summary>
	All
}

public enum TokenUsageStep
{
	Hour,
	Day
}

/// <summary>
///     The usage of one provider and model over one step of the report.
/// </summary>
/// <param name="Start">The start of the hour, or the local midnight of the day.</param>
/// <param name="Provider">The provider.</param>
/// <param name="Model">The model.</param>
/// <param name="Tokens">All the tokens of the step, priced or not.</param>
/// <param name="CostUsd">The cost of the priced buckets, or <c>null</c> when none is priced.</param>
/// <param name="CacheSavingsUsd">The cache savings of the priced buckets, or <c>null</c> when none is priced.</param>
/// <param name="UnpricedTokens">The tokens of the buckets without a price, left out of the cost.</param>
public sealed record TokenUsageRow(
	DateTimeOffset Start,
	Provider Provider,
	string Model,
	TokenCounts Tokens,
	double? CostUsd,
	double? CacheSavingsUsd,
	long UnpricedTokens);

/// <summary>
///     The token usage of a period, by step, provider and model, and the workstations available to the filter.
/// </summary>
public sealed record TokenUsageReport(
	DateTimeOffset From,
	DateTimeOffset To,
	TokenUsageStep Step,
	string TimeZone,
	IReadOnlyList<TokenUsageRow> Rows,
	IReadOnlyList<UsageMachine> Machines);
