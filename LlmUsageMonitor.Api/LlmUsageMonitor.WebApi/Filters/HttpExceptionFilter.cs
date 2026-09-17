using LlmUsageMonitor.Abstractions.Exceptions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace LlmUsageMonitor.Filters;

/// <summary>
///     Maps application exceptions to ProblemDetails; provider errors carry their stable <c>code</c> as an extension.
/// </summary>
public sealed class HttpExceptionFilter(ProblemDetailsFactory problems) : IExceptionFilter
{
	public void OnException(ExceptionContext context)
	{
		switch (context.Exception)
		{
			case RequestValidationException exception:
				var modelState = new ModelStateDictionary();
				foreach (var (field, messages) in exception.Errors)
				foreach (var message in messages)
					modelState.AddModelError(field, message);

				context.Result = new BadRequestObjectResult(problems.CreateValidationProblemDetails(context.HttpContext, modelState, StatusCodes.Status400BadRequest));
				break;
			case ResourceNotFoundException exception:
				context.Result = Problem(context, StatusCodes.Status404NotFound, "La ressource demandée n'existe pas.", exception.Message, null);
				break;
			case ProviderException { Code: ProviderErrorCodes.CliBusy } exception:
				context.Result = Problem(context, StatusCodes.Status409Conflict, "Un process CLI tourne déjà pour ce provider.", exception.Message, exception.Code);
				break;
			case ProviderException exception:
				context.Result = Problem(context, StatusCodes.Status502BadGateway, "Le service externe n'a pas répondu correctement.", exception.Message, exception.Code);
				break;
			default:
				return;
		}

		// Ajout de l'exception dans les tags de l'activité pour le suivi et le traçage
		var activity = context.HttpContext.Features[typeof(IHttpActivityFeature)] as IHttpActivityFeature;
		activity?.Activity.SetTag("exception", context.Exception);

		context.ExceptionHandled = true;
	}

	private ObjectResult Problem(ExceptionContext context, int status, string title, string detail, string? code)
	{
		var problem = problems.CreateProblemDetails(context.HttpContext, status, title, detail: detail);
		if (code is { })
		{
			problem.Extensions["code"] = code;
		}

		return new(problem) { StatusCode = status };
	}
}