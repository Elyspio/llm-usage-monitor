using LlmUsageMonitor.Abstractions.Data;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Interfaces.Adapters;
using LlmUsageMonitor.Abstractions.Interfaces.Repositories;
using LlmUsageMonitor.Abstractions.Interfaces.Services;
using LlmUsageMonitor.Core.Rules;
using Microsoft.Extensions.Logging;

namespace LlmUsageMonitor.Core.Services;

public sealed class TokenUsageService(
	ITokenUsageRepository usage,
	IUsageMachineRepository machines,
	IModelPriceRepository prices,
	TimeProvider time) : ITokenUsageService
{
	public async Task<TokenUsageUploadResult> Upload(TokenUsageUpload upload, CancellationToken cancellationToken)
	{
		var now = time.GetUtcNow();
		Validate(upload, now);

		var models = upload.Buckets.SelectMany(bucket => TokenPricing.Candidates(bucket.Model)).ToHashSet(StringComparer.Ordinal);
		var known = (await prices.Find(models, cancellationToken)).ToDictionary(price => price.Model, StringComparer.Ordinal);

		var buckets = upload.Buckets.Select(bucket => new TokenUsageBucket(
				upload.MachineId,
				bucket.Provider,
				bucket.Model,
				bucket.Hour.ToUniversalTime(),
				bucket.Tokens,
				TokenPricing.Resolve(bucket.Model, known) is { } price ? TokenPricing.Cost(bucket.Tokens, price) : null))
			.ToList();

		if (buckets.Count > 0)
		{
			await usage.Upsert(buckets, cancellationToken);
		}

		await machines.Save(new(upload.MachineId, upload.MachineName.Trim(), now), cancellationToken);

		return new(buckets.Count, buckets.Count(bucket => bucket.Cost is null));
	}

	public async Task<TokenUsageReport> Get(TokenUsageRange range, string? machineId, string? timeZone, CancellationToken cancellationToken)
	{
		var zone = FindTimeZone(timeZone);
		var to = time.GetUtcNow();
		var (from, step) = Period(range, to, zone);

		var buckets = await usage.Get(from, to, string.IsNullOrEmpty(machineId) ? null : machineId, cancellationToken);

		var rows = buckets
			.GroupBy(bucket => (Start: StepStart(bucket.Hour, step, zone), bucket.Provider, bucket.Model))
			.Select(group =>
			{
				var priced = group.Where(bucket => bucket.Cost is { }).ToList();
				return new TokenUsageRow(
					group.Key.Start,
					group.Key.Provider,
					group.Key.Model,
					group.Aggregate(TokenCounts.Zero, (total, bucket) => total.Add(bucket.Tokens)),
					priced.Count > 0 ? priced.Sum(bucket => bucket.Cost!.Usd) : null,
					priced.Count > 0 ? priced.Sum(bucket => bucket.Cost!.CacheSavingsUsd) : null,
					group.Where(bucket => bucket.Cost is null).Sum(bucket => Total(bucket.Tokens)));
			})
			.OrderBy(row => row.Start).ThenBy(row => row.Provider).ThenBy(row => row.Model, StringComparer.Ordinal)
			.ToList();

		var known = (await machines.GetAll(cancellationToken)).OrderBy(machine => machine.Name, StringComparer.OrdinalIgnoreCase).ToList();

		return new(from, to, step, zone.Id, rows, known);
	}

	/// <summary>
	///     The start of the period: the hour 23 hours before the current one, or the local midnight of the first day.
	/// </summary>
	internal static (DateTimeOffset From, TokenUsageStep Step) Period(TokenUsageRange range, DateTimeOffset now, TimeZoneInfo zone)
	{
		if (range == TokenUsageRange.Last24Hours)
		{
			return (TruncateToHour(now).AddHours(-23), TokenUsageStep.Hour);
		}

		var days = range switch
		{
			TokenUsageRange.Last7Days => 7,
			TokenUsageRange.Last30Days => 30,
			_ => 90
		};
		var firstDay = TimeZoneInfo.ConvertTime(now, zone).Date.AddDays(1 - days);
		return (LocalMidnight(firstDay, zone), TokenUsageStep.Day);
	}

	private static DateTimeOffset StepStart(DateTimeOffset hour, TokenUsageStep step, TimeZoneInfo zone)
	{
		return step == TokenUsageStep.Hour ? hour : LocalMidnight(TimeZoneInfo.ConvertTime(hour, zone).Date, zone);
	}

	private static DateTimeOffset LocalMidnight(DateTime date, TimeZoneInfo zone)
	{
		return new(date, zone.GetUtcOffset(date));
	}

	private static DateTimeOffset TruncateToHour(DateTimeOffset value)
	{
		return new(value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerHour, TimeSpan.Zero);
	}

	private static long Total(TokenCounts tokens)
	{
		return tokens.Input + tokens.CacheRead + tokens.CacheWrite + tokens.Output;
	}

	private static TimeZoneInfo FindTimeZone(string? timeZone)
	{
		if (string.IsNullOrEmpty(timeZone))
		{
			return TimeZoneInfo.Utc;
		}

		try
		{
			return TimeZoneInfo.FindSystemTimeZoneById(timeZone);
		}
		catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
		{
			throw new RequestValidationException(new Dictionary<string, string[]> { ["timeZone"] = ["Fuseau IANA attendu, par exemple Europe/Paris."] });
		}
	}

	private static void Validate(TokenUsageUpload upload, DateTimeOffset now)
	{
		var errors = new Dictionary<string, string[]>();

		if (string.IsNullOrWhiteSpace(upload.MachineId) || upload.MachineId.Length > TokenUsageUpload.MaxIdLength)
		{
			errors["machineId"] = [$"Obligatoire, {TokenUsageUpload.MaxIdLength} caractères au plus."];
		}

		if (string.IsNullOrWhiteSpace(upload.MachineName) || upload.MachineName.Trim().Length > TokenUsageUpload.MaxIdLength)
		{
			errors["machineName"] = [$"Obligatoire, {TokenUsageUpload.MaxIdLength} caractères au plus."];
		}

		if (upload.Buckets.Count > TokenUsageUpload.MaxBuckets)
		{
			errors["buckets"] = [$"{TokenUsageUpload.MaxBuckets} buckets au plus par envoi."];
			throw new RequestValidationException(errors);
		}

		var seen = new HashSet<(Provider, string, DateTimeOffset)>();
		for (var index = 0; index < upload.Buckets.Count; index++)
		{
			var bucket = upload.Buckets[index];
			var field = $"buckets[{index}]";

			if (string.IsNullOrWhiteSpace(bucket.Model) || bucket.Model.Length > TokenUsageUpload.MaxModelLength)
			{
				errors[$"{field}.model"] = [$"Obligatoire, {TokenUsageUpload.MaxModelLength} caractères au plus."];
			}

			if (bucket.Hour.UtcTicks % TimeSpan.TicksPerHour != 0 || bucket.Hour > now)
			{
				errors[$"{field}.hour"] = ["Début d'heure UTC attendu, pas dans le futur."];
			}

			if (bucket.Tokens is null || bucket.Tokens.Input < 0 || bucket.Tokens.CacheRead < 0 || bucket.Tokens.CacheWrite < 0 || bucket.Tokens.Output < 0)
			{
				errors[$"{field}.tokens"] = ["Compteurs positifs ou nuls attendus."];
			}

			if (!seen.Add((bucket.Provider, bucket.Model, bucket.Hour.ToUniversalTime())))
			{
				errors[field] = ["Bucket en double dans l'envoi."];
			}
		}

		if (errors.Count > 0)
		{
			throw new RequestValidationException(errors);
		}
	}
}

public sealed class ModelPriceService(IModelPriceSource source, IModelPriceRepository prices, ILogger<ModelPriceService> logger) : IModelPriceService
{
	public async Task Refresh(CancellationToken cancellationToken)
	{
		var fetched = await source.Fetch(cancellationToken);
		if (fetched.Count == 0)
		{
			// An empty table is a broken download, never a reason to forget the known prices.
			throw new InvalidOperationException("The model price table is empty.");
		}

		await prices.Save(fetched, cancellationToken);
		logger.LogInformation("{Count} model prices refreshed", fetched.Count);
	}
}
