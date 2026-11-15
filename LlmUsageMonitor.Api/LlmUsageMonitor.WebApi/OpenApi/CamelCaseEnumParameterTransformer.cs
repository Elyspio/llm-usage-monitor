using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace LlmUsageMonitor.OpenApi;

/// <summary>
///     Since .NET 11 an enum route or query parameter is inlined with its C# names (<c>Claude</c>), while the JSON and the
///     generated client use camelCase (<c>claude</c>). Model binding is case-insensitive: both are accepted, the document
///     keeps camelCase.
/// </summary>
public sealed class CamelCaseEnumParameterTransformer : IOpenApiSchemaTransformer
{
	public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
	{
		if (context.ParameterDescription is not null && (Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type).IsEnum && schema.Enum is { Count: > 0 } values)
		{
			schema.Enum = [.. values.Select(value => (JsonNode)JsonNamingPolicy.CamelCase.ConvertName(value!.GetValue<string>()))];
		}

		return Task.CompletedTask;
	}
}
