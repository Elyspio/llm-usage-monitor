using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hangfire;
using Hangfire.InMemory;
using LlmUsageMonitor.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace LlmUsageMonitor.WebApi.Tests;

/// <summary>
///     The API with Hangfire configured on an in-memory storage, without any job server: the dashboard is mapped, nothing runs.
/// </summary>
public sealed class HangfireDashboardApiFactory : ApiFactory
{
	public const string AuthorizationEndpoint = $"{Issuer}/protocol/openid-connect/auth";

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		base.ConfigureWebHost(builder);
		builder.ConfigureTestServices(services =>
		{
			services.AddHangfire(configuration => configuration.UseStorage(new InMemoryStorage()));
			// A static configuration keeps the OIDC handler from fetching the Keycloak metadata on a challenge.
			services.Configure<OpenIdConnectOptions>(HangfireDashboardAuthentication.OidcScheme, options =>
				options.Configuration = new() { Issuer = Issuer, AuthorizationEndpoint = AuthorizationEndpoint });
		});
	}

	/// <summary>
	///     A browser-like client on https (the cookies are Secure), which does not follow the redirects, signed in to the
	///     dashboard with the given client roles when any is given.
	/// </summary>
	public HttpClient CreateDashboardClient(params string[]? clientRoles)
	{
		var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
		if (clientRoles is { })
		{
			client.DefaultRequestHeaders.Add("Cookie", $"{HangfireDashboardAuthentication.CookieName}={CreateSignInCookie(clientRoles)}");
		}

		return client;
	}

	/// <summary>The cookie the OIDC sign-in writes: the id token claims plus the resource_access claim of the access token.</summary>
	private string CreateSignInCookie(string[] clientRoles)
	{
		var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(HangfireDashboardAuthentication.CookieScheme);
		var resourceAccess = JsonSerializer.Serialize(new Dictionary<string, object> { [ClientId] = new Dictionary<string, object> { ["roles"] = clientRoles } });
		var identity = new ClaimsIdentity([new("sub", "test-user"), new("resource_access", resourceAccess, "JSON")], HangfireDashboardAuthentication.CookieScheme);
		var now = DateTimeOffset.UtcNow;
		var ticket = new AuthenticationTicket(new(identity), new() { IssuedUtc = now, ExpiresUtc = now + HangfireDashboardAuthentication.CookieLifetime },
			HangfireDashboardAuthentication.CookieScheme);
		return options.TicketDataFormat.Protect(ticket);
	}
}

public sealed partial class HangfireDashboardTests(HangfireDashboardApiFactory factory) : IClassFixture<HangfireDashboardApiFactory>
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task An_anonymous_visit_is_sent_to_the_keycloak_sign_in()
	{
		using var client = factory.CreateDashboardClient(null);

		var response = await client.GetAsync("/hangfire", Token);

		response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
		response.Headers.Location!.ToString().ShouldStartWith(HangfireDashboardApiFactory.AuthorizationEndpoint);
	}

	[Fact]
	public async Task A_bearer_token_does_not_open_the_dashboard()
	{
		using var client = factory.CreateDashboardClient(null);
		client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.CreateToken([ApiFactory.AdminRole]));

		var response = await client.GetAsync("/hangfire", Token);

		response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
	}

	[Fact]
	public async Task A_signed_in_user_without_the_admin_role_is_forbidden()
	{
		using var client = factory.CreateDashboardClient();

		var page = await client.GetAsync("/hangfire", Token);
		var action = await client.PostAsync("/hangfire/recurring/trigger", new FormUrlEncodedContent([new("jobs[]", "poll-claude")]), Token);

		page.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
		action.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task The_admin_sees_the_dashboard_with_an_antiforgery_token()
	{
		using var client = factory.CreateDashboardClient(ApiFactory.AdminRole);

		var response = await client.GetAsync("/hangfire", Token);

		response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
		AntiforgeryToken().IsMatch(await response.Content.ReadAsStringAsync(Token)).ShouldBeTrue();
		response.Headers.GetValues("Set-Cookie").ShouldContain(cookie => cookie.StartsWith($"{HangfireDashboardAuthentication.AntiforgeryCookieName}="));
	}

	[Fact]
	public async Task A_dashboard_action_without_antiforgery_token_is_rejected()
	{
		using var client = factory.CreateDashboardClient(ApiFactory.AdminRole);

		var response = await client.PostAsync("/hangfire/recurring/trigger", new FormUrlEncodedContent([new("jobs[]", "poll-claude")]), Token);

		response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task A_dashboard_action_with_the_antiforgery_token_is_accepted()
	{
		using var client = factory.CreateDashboardClient(ApiFactory.AdminRole);
		var page = await client.GetAsync("/hangfire", Token);
		var match = AntiforgeryToken().Match(await page.Content.ReadAsStringAsync(Token));
		var antiforgeryCookie = page.Headers.GetValues("Set-Cookie").Single(cookie => cookie.StartsWith($"{HangfireDashboardAuthentication.AntiforgeryCookieName}=")).Split(';')[0];

		using var request = new HttpRequestMessage(HttpMethod.Post, "/hangfire/recurring/trigger") { Content = new FormUrlEncodedContent([new("jobs[]", "poll-claude")]) };
		request.Headers.Add("Cookie", antiforgeryCookie);
		request.Headers.Add(match.Groups["header"].Value, WebUtility.HtmlDecode(match.Groups["token"].Value));
		var response = await client.SendAsync(request, Token);

		response.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
		((int)response.StatusCode).ShouldBeLessThan(500);
	}

	[Fact]
	public void The_sign_in_cookie_is_short_lived_fixed_and_unreachable_from_scripts()
	{
		var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(HangfireDashboardAuthentication.CookieScheme);

		options.ExpireTimeSpan.ShouldBe(TimeSpan.FromHours(8));
		options.SlidingExpiration.ShouldBeFalse();
		options.Cookie.HttpOnly.ShouldBeTrue();
		options.Cookie.SecurePolicy.ShouldBe(CookieSecurePolicy.Always);
		options.Cookie.SameSite.ShouldBe(SameSiteMode.Lax);
	}

	/// <summary>The meta tags read by the dashboard script, which sends the token in that header with every action.</summary>
	[GeneratedRegex("<meta name=\"csrf-header\" content=\"(?<header>[^\"]+)\">\\s*<meta name=\"csrf-token\" content=\"(?<token>[^\"]+)\">")]
	private static partial Regex AntiforgeryToken();
}
