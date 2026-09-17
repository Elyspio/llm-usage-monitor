using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using LlmUsageMonitor.Abstractions.Exceptions;

namespace LlmUsageMonitor.Adapters.Codex;

public sealed record ServerNotification(string Method, JsonElement Params);

/// <summary>
///     A JSON-RPC error returned by the app-server.
/// </summary>
public sealed class CodexRpcException(JsonElement error)
	: Exception(error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String ? message.GetString() : error.GetRawText());

/// <summary>
///     One <c>codex app-server --listen stdio://</c> process: newline-delimited JSON-RPC requests, responses and notifications.
///     stdin stays open for the protocol; the process is killed on dispose.
/// </summary>
internal sealed class CodexAppServer : IAsyncDisposable
{
	private readonly Task _errors;
	private readonly Channel<ServerNotification> _notifications = Channel.CreateUnbounded<ServerNotification>();
	private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
	private readonly Process _process;
	private readonly Task _reader;
	private readonly SemaphoreSlim _writeLock = new(1, 1);
	private long _nextId;

	private CodexAppServer(Process process)
	{
		_process = process;
		_reader = Task.Run(ReadMessages);
		// Diagnostics are drained, never logged: they may contain account details.
		_errors = process.StandardError.ReadToEndAsync();
	}

	public ChannelReader<ServerNotification> Notifications => _notifications.Reader;

	public async ValueTask DisposeAsync()
	{
		_process.StandardInput.Close();
		if (!_process.HasExited)
		{
			_process.Kill(true);
		}

		await Task.WhenAny(Task.WhenAll(_reader, _errors), Task.Delay(TimeSpan.FromSeconds(5)));
		_process.Dispose();
		_writeLock.Dispose();
	}

	public static async Task<CodexAppServer> Start(CodexOptions options, CancellationToken cancellationToken)
	{
		var info = new ProcessStartInfo(options.Executable)
		{
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
			WorkingDirectory = options.ResolveWorkingDirectory(),
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		info.ArgumentList.Add("app-server");
		info.ArgumentList.Add("--listen");
		info.ArgumentList.Add("stdio://");

		Process process;
		try
		{
			process = Process.Start(info) ?? throw new ProviderException(ProviderErrorCodes.CliUnavailable, "Cannot start Codex.");
		}
		catch (Win32Exception exception)
		{
			throw new ProviderException(ProviderErrorCodes.CliUnavailable, "Cannot start Codex. Install its CLI or set Codex:Executable to its native executable.", exception);
		}

		var server = new CodexAppServer(process);
		try
		{
			await server.Request("initialize", new { clientInfo = new { name = "llm_usage_monitor", title = "LLM Usage Monitor", version = "1.0.0" } }, cancellationToken);
			await server.Notify("initialized", new { });
			return server;
		}
		catch
		{
			await server.DisposeAsync();
			throw;
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
			throw new ProviderException(ProviderErrorCodes.CliExited, "Codex closed its input before answering.", exception);
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
			var exited = new ProviderException(ProviderErrorCodes.CliExited, "Codex exited before answering.");
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