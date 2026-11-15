using Elyspio.Utils.Results;
using Shouldly;

namespace LlmUsageMonitor.Adapters.Tests;

/// <summary>Assertions on the results of the services and adapters.</summary>
internal static class ResultShould
{
	public static T ShouldSucceed<T, TError>(this Result<T, TError> result) where TError : notnull
	{
		result.TryGet(out var value, out var error).ShouldBeTrue($"Failed with {error}");
		return value!;
	}

	public static TError ShouldFail<T, TError>(this Result<T, TError> result) where TError : notnull
	{
		result.TryGet(out var value, out var error).ShouldBeFalse($"Succeeded with {value}");
		return error!;
	}

	public static T ShouldSucceed<T>(this Result<T> result)
	{
		return ((Result<T, Error>)result).ShouldSucceed();
	}

	public static Error ShouldFail<T>(this Result<T> result)
	{
		return ((Result<T, Error>)result).ShouldFail();
	}
}
