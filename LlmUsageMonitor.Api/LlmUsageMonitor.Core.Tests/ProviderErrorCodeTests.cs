using LlmUsageMonitor.Abstractions.Exceptions;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.Core.Tests;

public sealed class ProviderErrorCodeTests
{
	[Theory]
	[InlineData(ProviderErrorCode.AuthExpired, "AUTH_EXPIRED")]
	[InlineData(ProviderErrorCode.HttpError, "HTTP_ERROR")]
	[InlineData(ProviderErrorCode.CliUnsupportedOption, "CLI_UNSUPPORTED_OPTION")]
	[InlineData(ProviderErrorCode.UnexpectedError, "UNEXPECTED_ERROR")]
	[InlineData(ProviderErrorCode.NotificationFailed, "NOTIFICATION_FAILED")]
	public void Stored_codes_keep_the_upper_snake_case_form_of_the_runs_already_in_the_database(ProviderErrorCode code, string stored)
	{
		code.ToStoredCode().ShouldBe(stored);
	}

	[Fact]
	public void Every_code_is_read_back_from_its_stored_form()
	{
		foreach (var code in Enum.GetValues<ProviderErrorCode>())
		{
			ProviderErrorCodeStorage.ParseStoredCode(code.ToStoredCode()).ShouldBe(code);
		}
	}

	[Theory]
	[InlineData("SOMETHING_NEW")]
	[InlineData("")]
	[InlineData(null)]
	public void A_code_this_version_does_not_know_reads_as_an_unexpected_error(string? stored)
	{
		ProviderErrorCodeStorage.ParseStoredCode(stored).ShouldBe(ProviderErrorCode.UnexpectedError);
	}
}
