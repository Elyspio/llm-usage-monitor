using System.Diagnostics;
using Elyspio.Utils.Results;
using Elyspio.Utils.Results.AspNetCore;
using Microsoft.AspNetCore.Mvc;

namespace LlmUsageMonitor.Controllers;

/// <summary>
///     Turns the results of the services into controller responses: the value on success, the ProblemDetails of the error
///     otherwise (<c>code</c> extension, ValidationProblemDetails for an invalid request). The actions keep their
///     <c>ActionResult&lt;T&gt;</c> return type, so the OpenAPI document still describes the success body.
/// </summary>
public static class ResultActions
{
	public static ActionResult<T> ToActionResult<T>(this Result<T> result)
	{
		return result.TryGet(out var value, out var error) ? value : error.ToActionResult();
	}

	public static ActionResult ToActionResult(this Result<Unit> result)
	{
		return result.TryGet(out _, out var error) ? new NoContentResult() : error.ToActionResult();
	}

	/// <summary>
	///     Through an <see cref="ObjectResult" />: executing the <see cref="IResult" /> in a controller goes through the MVC
	///     problem writer, which serializes the declared <see cref="ProblemDetails" /> type and drops the validation <c>errors</c>.
	/// </summary>
	public static ActionResult ToActionResult(this Error error)
	{
		var problem = (ProblemDetails)((IValueHttpResult)error.ToProblem()).Value!;
		return new ProblemResult(problem) { StatusCode = problem.Status, ContentTypes = { "application/problem+json" } };
	}

	/// <summary>Adds the <c>traceId</c> the ASP.NET problem details service would have added.</summary>
	private sealed class ProblemResult(ProblemDetails problem) : ObjectResult(problem)
	{
		public override void OnFormatting(ActionContext context)
		{
			problem.Extensions.TryAdd("traceId", Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
			base.OnFormatting(context);
		}
	}
}
