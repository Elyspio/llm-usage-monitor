using MongoDB.Bson;
using MongoDB.Bson.Serialization.Conventions;

namespace LlmUsageMonitor.Adapters.MongoDB;

internal static class Collections
{
	public const string UsageSnapshots = "usageSnapshots";
	public const string Resets = "resets";
	public const string TriggerRuns = "triggerRuns";
	public const string ProviderStates = "providerStates";
	public const string Settings = "settings";
	public const string DataProtectionKeys = "dataProtectionKeys";
}

internal static class MongoConventions
{
	private static int _registered;

	/// <summary>
	///     camelCase fields, enums as strings and tolerance to unknown fields for the documents of this adapter.
	/// </summary>
	public static void Register()
	{
		if (Interlocked.Exchange(ref _registered, 1) == 1)
		{
			return;
		}

		var pack = new ConventionPack
		{
			new CamelCaseElementNameConvention(),
			new EnumRepresentationConvention(BsonType.String),
			new IgnoreExtraElementsConvention(true)
		};
		ConventionRegistry.Register("llm-usage-monitor", pack, type => type.Namespace?.StartsWith("LlmUsageMonitor", StringComparison.Ordinal) == true);
	}

	public static DateTimeOffset ToOffset(this DateTime value)
	{
		return new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
	}

	public static DateTimeOffset? ToOffset(this DateTime? value)
	{
		return value?.ToOffset();
	}

	public static DateTime ToUtc(this DateTimeOffset value)
	{
		return value.UtcDateTime;
	}

	public static DateTime? ToUtc(this DateTimeOffset? value)
	{
		return value?.UtcDateTime;
	}
}