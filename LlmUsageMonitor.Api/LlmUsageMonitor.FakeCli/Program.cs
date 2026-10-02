using System.Text.Json;
using System.Text.Json.Nodes;

// A stand-in for the claude and codex CLIs. It reads its scenario from fake-cli.json in its working directory, keyed by the
// first argument ("-p", "mcp", "--version", "app-server"), and appends each invocation to invocations.log and each JSON-RPC
// message it receives to requests.log, next to the scenario.
//
// A command: { "stdout": "...", "stderr": "...", "exitCode": 0, "writeFile": { "path": "...", "content": "..." } }.
// "app-server" without "exitCode" serves JSON-RPC on stdio: "rpc" maps a method to { "result": ... }, { "error": ... } or
// { "silent": true } (never answered), with optional "notifications" sent after the response.

var directory = Environment.CurrentDirectory;
var scenario = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "fake-cli.json")))!.AsObject();
File.AppendAllText(Path.Combine(directory, "invocations.log"), string.Join(' ', args) + Environment.NewLine);

var key = args.Length == 0 ? "" : args[0];
var command = scenario["commands"]?[key]?.AsObject() ?? new JsonObject();

if (key == "app-server" && command["exitCode"] is null)
{
	return Serve(scenario["rpc"]?.AsObject() ?? new JsonObject(), Path.Combine(directory, "requests.log"));
}

if (command["writeFile"] is JsonObject file)
{
	File.WriteAllText((string)file["path"]!, (string)file["content"]!);
}

Console.Out.Write((string?)command["stdout"] ?? "");
Console.Error.Write((string?)command["stderr"] ?? "");
return (int?)command["exitCode"] ?? 0;

static int Serve(JsonObject rpc, string requestsLog)
{
	while (Console.In.ReadLine() is { } line)
	{
		File.AppendAllText(requestsLog, line + Environment.NewLine);
		var message = JsonNode.Parse(line)!.AsObject();
		if (message["id"] is not { } id)
		{
			continue;
		}

		var behavior = rpc[(string)message["method"]!]?.AsObject() ?? new JsonObject { ["result"] = new JsonObject() };
		if (behavior["silent"] is { })
		{
			continue;
		}

		var response = new JsonObject { ["id"] = id.DeepClone() };
		if (behavior["error"] is { } error)
		{
			response["error"] = error.DeepClone();
		}
		else
		{
			response["result"] = behavior["result"]?.DeepClone() ?? new JsonObject();
		}

		Write(response);
		foreach (var notification in behavior["notifications"]?.AsArray() ?? []) Write(notification!.DeepClone());
	}

	return 0;
}

static void Write(JsonNode message)
{
	Console.Out.WriteLine(message.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
	Console.Out.Flush();
}
