using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using LlmUsageMonitor.Abstractions.Exceptions;
using LlmUsageMonitor.Abstractions.Helpers;
using Microsoft.Extensions.Logging;

namespace LlmUsageMonitor.Adapters.Codex;

public sealed record ServerNotification(string Method, JsonElement Params);

/// <summary>
///     A JSON-RPC error returned by the app-server: <c>code</c>, <c>message</c> and optional <c>data</c>.
/// </summary>
public sealed class CodexRpcException(JsonElement error)
	: Exception(error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String ? message.GetString() : error.GetRawText())
{
	public const int MethodNotFound = -32601;
	public const int InvalidParams = -32602;

	public int? Code { get; } = error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var value) ? value : null;

	/// <summary>The <c>data</c> member, <c>Undefined</c> when absent.</summary>
	public JsonElement ErrorData { get; } = error.TryGetProperty("data", out var data) ? data.Clone() : default;

	/// <summary>The method or its parameters are unknown to this CLI version: an update changed the protocol.</summary>
	public bool IsProtocolMismatch => Code is MethodNotFound or InvalidParams;
}

/// <summary>
///     One <c>codex app-server --listen stdio://</c> process: newline-delimited JSON-RPC requests, responses and notifications.
///     stdin stays open for the protocol; the process is killed on dispose, which logs its run.
/// </summary>
internal sealed class CodexAppServer : IAsyncDisposable
{
	private readonly Task<string> _errors;
	private readonly ILogger _logger;
	private readonly Channel<ServerNotification> _notifications = Channel.CreateUnbounded<ServerNotification>();
	private readonly string _operation;
	private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
	private readonly Process _process;
	private readonly Task _reader;
	private readonly Stopwatch _watch = Stopwatch.StartNew();
	private readonly SemaphoreSlim _writeLock = new(1, 1);
	private long _nextId;

	private CodexAppServer(Process process, string operation, ILogger logger)
	{
		_process = process;
		_operation = operation;
		_logger = logger;
		_reader = Task.Run(ReadMessages);
		_errors = process.StandardError.ReadToEndAsync();
	}

	public ChannelReader<ServerNotification> Notifications => _notifications.Reader;

	public async ValueTask DisposeAsync()
	{
		try
		{
			// Still running when the client is done: the normal case. Exited earlier: the server stopped on its own.
			var exitedOnItsOwn = _process.HasExited;
			try
			{
				_process.StandardInput.Close();
			}
			catch (IOException)
			{
				// The pipe is already broken: the process exited.
			}

			CliProcess.KillTree(_process);
			await Task.WhenAny(Task.WhenAll(_reader, _errors), Task.Delay(TimeSpan.FromSeconds(5)));
			Log(exitedOnItsOwn);
		}
		finally
		{
			_process.Dispose();
			_writeLock.Dispose();
		}
	}

	/// <param name="operation">What the server is started for, for the logs.</param>
	public static async Task<CodexAppServer> Start(CodexOptions options, string operation, ILogger logger, CancellationToken cancellationToken)
	{
		var process = CliProcess.Start(options.Executable, ["app-server", "--listen", "stdio://"], options.ResolveWorkingDirectory(), logger,
			"Install its CLI or set Codex:Executable to its native executable.");

		var server = new CodexAppServer(process, operation, logger);
		try
		{
			await server.Request("initialize", new { clientInfo = new { name = "llm_usage_monitor", title = "LLM Usage Monitor", version = "1.0.0" } }, cancellationToken);
			await server.Notify("initialized", new { });
			return server;
		}
		catch (ProviderException exception) when (exception.Code == ProviderErrorCode.CliExited)
		{
			// Exited before answering: an argument the CLI no longer accepts is reported with its version.
			var stderr = await server.ReadErrors();
			await server.DisposeAsync();
			if (CliProcess.IsUnsupportedOption(stderr))
			{
				var version = await CliProcess.ReadVersion(options.Executable, options.ResolveWorkingDirectory(), logger, cancellationToken);
				throw new ProviderException(ProviderErrorCode.CliUnsupportedOption, $"{CliProcess.Truncate(stderr)} (codex {version}: update the arguments of CodexAppServer)");
			}

			throw;
		}
		catch
		{
			await server.DisposeAsync();
			throw;
		}
	}

	private async Task<string> ReadErrors()
	{
		var completed = await Task.WhenAny(_errors, Task.Delay(TimeSpan.FromSeconds(2)));
		return completed == _errors && _errors.IsCompletedSuccessfully ? _errors.Result : "";
	}

	private void Log(bool exitedOnItsOwn)
	{
		var stderr = _errors.IsCompletedSuccessfully ? CliProcess.Truncate(_errors.Result) : "";
		if (exitedOnItsOwn)
		{
			_logger.LogWarning("codex app-server ({Operation}) exited on its own with code {ExitCode} after {DurationMs} ms: {StandardError}", _operation, _process.ExitCode,
				_watch.ElapsedMilliseconds, stderr);
			return;
		}

		_logger.LogInformation("codex app-server ({Operation}) stopped after {DurationMs} ms", _operation, _watch.ElapsedMilliseconds);
		if (stderr.Length > 0)
		{
			// stderr may hold account details: debug level only.
			_logger.LogDebug("codex app-server ({Operation}) stderr: {StandardError}", _operation, stderr);
		}
	}

	public async Task<JsonElement> Request(string method, object parameters, CancellationToken cancellationToken)
	{
		var id = Interlocked.Increment(ref _nextId);
		var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pending[id] = completion;

		await Write(new { id, method, @params = parameters });

		await using (cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken)))
		{
			return await completion.Task;
		}
	}

	public Task Notify(string method, object parameters)
	{
		return Write(new { method, @params = parameters });
	}

	private async Task Write(object message)
	{
		var line = JsonSerializer.Serialize(message);
		await _writeLock.WaitAsync();
		try
		{
			await _process.StandardInput.WriteLineAsync(line);
			await _process.StandardInput.FlushAsync();
		}
		catch (IOException exception)
		{
			throw new ProviderException(ProviderErrorCode.CliExited, "Codex closed its input before answering.", exception);
		}
		finally
		{
			_writeLock.Release();
		}
	}

	private async Task ReadMessages()
	{
		try
		{
			while (await _process.StandardOutput.ReadLineAsync() is { } line)
			{
				if (TryParse(line) is not { } message)
				{
					continue;
				}

				var hasMethod = message.TryGetProperty("method", out var method);
				var hasId = message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number;

				if (hasId && !hasMethod)
				{
					if (!_pending.TryRemove(id.GetInt64(), out var completion))
					{
						continue;
					}

					if (message.TryGetProperty("error", out var error))
					{
						completion.TrySetException(new CodexRpcException(error.Clone()));
					}
					else
					{
						completion.TrySetResult(message.TryGetProperty("result", out var result) ? result.Clone() : default);
					}
				}
				else if (hasMethod && !hasId)
				{
					var parameters = message.TryGetProperty("params", out var value) ? value.Clone() : default;
					_notifications.Writer.TryWrite(new(method.GetString() ?? string.Empty, parameters));
				}
			}
		}
		finally
		{
			var exited = new ProviderException(ProviderErrorCode.CliExited, "Codex exited before answering.");
			foreach (var completion in _pending.Values) completion.TrySetException(exited);
			_notifications.Writer.TryComplete(exited);
		}
	}

	private static JsonElement? TryParse(string line)
	{
		try
		{
			using var document = JsonDocument.Parse(line);
			return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
		}
		catch (JsonException)
		{
			// Not a protocol message (e.g. a stray log line): ignored.
			return null;
		}
	}
}