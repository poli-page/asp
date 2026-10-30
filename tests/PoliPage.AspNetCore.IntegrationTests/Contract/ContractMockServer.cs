using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PoliPage.AspNetCore.IntegrationTests.Contract;

/// <summary>
/// One answer of the mock: an HTTP answer, or a fault. The contract's faults are <c>drop</c>
/// (close the connection without an answer) and <c>hang</c> (never answer).
/// </summary>
internal sealed record MockStep(
    int Status,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    byte[] Body,
    string? Fault);

/// <summary>A request the mock received: method, request target (path and query) and headers.</summary>
internal sealed record RecordedRequest(
    string Method,
    string Target,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers)
{
    public IReadOnlyList<string>? Header(string name)
        => Headers.TryGetValue(name, out var values) ? values : null;
}

/// <summary>
/// A minimal HTTP/1.1 server on a real loopback socket (127.0.0.1, a free port). The n-th
/// request it receives gets the n-th step. Connections are kept alive between answers, as a
/// real API does. Each request is recorded once it is fully read.
/// </summary>
internal sealed class ContractMockServer : IAsyncDisposable
{
    private const string ExhaustedBody = "{\"error\":\"MOCK_EXHAUSTED\"}";

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<RecordedRequest> _requests = [];
    private readonly List<string> _problems = [];
    private readonly List<TcpClient> _clients = [];
    private readonly object _gate = new();
    private readonly Task _acceptLoop;
    private IReadOnlyList<MockStep> _steps = [];

    public ContractMockServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Origin = string.Create(
            CultureInfo.InvariantCulture,
            $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");
        _acceptLoop = AcceptLoopAsync();
    }

    /// <summary>The server's origin, which replaces the contract's <c>{{mock}}</c> placeholder.</summary>
    public string Origin { get; }

    /// <summary>The answers, in order. Set before the first request arrives.</summary>
    public IReadOnlyList<MockStep> Steps
    {
        get
        {
            lock (_gate)
                return _steps;
        }

        set
        {
            lock (_gate)
                _steps = value;
        }
    }

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_gate)
                return [.. _requests];
        }
    }

    public IReadOnlyList<string> Problems
    {
        get
        {
            lock (_gate)
                return [.. _problems];
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        lock (_gate)
        {
            foreach (var client in _clients)
                client.Dispose();
        }

        try
        {
            await _acceptLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }

        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            lock (_gate)
                _clients.Add(client);

            _ = ServeConnectionAsync(client);
        }
    }

    private async Task ServeConnectionAsync(TcpClient client)
    {
        try
        {
            using (client)
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                var reader = new RequestReader(stream);
                while (!_stop.IsCancellationRequested)
                {
                    var request = await reader.ReadAsync(_stop.Token).ConfigureAwait(false);
                    if (request is null)
                        return; // the client closed the connection

                    if (!await AnswerAsync(stream, request).ConfigureAwait(false))
                        return;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // The client went away: nothing to answer.
        }
        catch (InvalidDataException ex)
        {
            lock (_gate)
                _problems.Add($"mock server: {ex.Message}");
        }
    }

    /// <summary>Answers one request; <see langword="false"/> when the connection must close.</summary>
    private async Task<bool> AnswerAsync(NetworkStream stream, RecordedRequest request)
    {
        int index;
        lock (_gate)
        {
            _requests.Add(request);
            index = _requests.Count - 1;
        }

        var steps = Steps;
        if (index >= steps.Count)
        {
            // More requests than the case has answers: the request list shows it.
            await WriteAnswerAsync(
                    stream,
                    new MockStep(599, [new("Content-Type", "application/json")], Encoding.UTF8.GetBytes(ExhaustedBody), null))
                .ConfigureAwait(false);
            return true;
        }

        var step = steps[index];
        switch (step.Fault)
        {
            case "drop":
                return false;
            case "hang":
                await WaitForCloseAsync(stream).ConfigureAwait(false);
                return false;
            default:
                await WriteAnswerAsync(stream, step).ConfigureAwait(false);
                return !WantsClose(request);
        }
    }

    private async Task WaitForCloseAsync(NetworkStream stream)
    {
        var buffer = new byte[1024];
        while (await stream.ReadAsync(buffer, _stop.Token).ConfigureAwait(false) > 0)
        {
            // Discard anything the client sends: it gets no answer.
        }
    }

    private async Task WriteAnswerAsync(NetworkStream stream, MockStep step)
    {
        var head = new StringBuilder();
        head.Append(CultureInfo.InvariantCulture, $"HTTP/1.1 {step.Status} Mock\r\n");
        foreach (var (name, value) in step.Headers)
            head.Append(CultureInfo.InvariantCulture, $"{name}: {value}\r\n");
        var hasBody = step.Status is not (204 or 304);
        if (hasBody)
            head.Append(CultureInfo.InvariantCulture, $"Content-Length: {step.Body.Length}\r\n");
        head.Append("\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), _stop.Token).ConfigureAwait(false);
        if (hasBody)
            await stream.WriteAsync(step.Body, _stop.Token).ConfigureAwait(false);
        await stream.FlushAsync(_stop.Token).ConfigureAwait(false);
    }

    private static bool WantsClose(RecordedRequest request)
        => request.Header("Connection") is { } values
            && values.Any(v => v.Contains("close", StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads HTTP/1.1 requests (Content-Length or chunked bodies) off one connection.</summary>
    private sealed class RequestReader(NetworkStream stream)
    {
        private const int MaxHeadBytes = 64 * 1024;
        private readonly byte[] _buffer = new byte[16 * 1024];
        private readonly List<byte> _pending = [];

        public async Task<RecordedRequest?> ReadAsync(CancellationToken cancellationToken)
        {
            var head = await ReadHeadAsync(cancellationToken).ConfigureAwait(false);
            if (head is null)
                return null;

            var lines = head.Split("\r\n");
            var requestLine = lines[0].Split(' ');
            if (requestLine.Length != 3)
                throw new InvalidDataException($"bad request line: {lines[0]}");

            var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                var colon = line.IndexOf(':', StringComparison.Ordinal);
                if (colon <= 0)
                    throw new InvalidDataException($"bad header line: {line}");
                var name = line[..colon].Trim();
                if (!headers.TryGetValue(name, out var values))
                    headers[name] = values = [];
                values.Add(line[(colon + 1)..].Trim());
            }

            await SkipBodyAsync(headers, cancellationToken).ConfigureAwait(false);
            return new RecordedRequest(
                requestLine[0],
                requestLine[1],
                headers.ToDictionary(h => h.Key, h => (IReadOnlyList<string>)h.Value, StringComparer.OrdinalIgnoreCase));
        }

        private async Task<string?> ReadHeadAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                var end = IndexOfHeadEnd();
                if (end >= 0)
                {
                    var head = Encoding.ASCII.GetString([.. _pending.Take(end)]);
                    _pending.RemoveRange(0, end + 4);
                    return head;
                }

                if (_pending.Count > MaxHeadBytes)
                    throw new InvalidDataException("request head too large");
                if (!await FillAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (_pending.Count == 0)
                        return null;
                    throw new InvalidDataException("connection closed mid-request");
                }
            }
        }

        private async Task SkipBodyAsync(Dictionary<string, List<string>> headers, CancellationToken cancellationToken)
        {
            if (headers.TryGetValue("Transfer-Encoding", out var te)
                && te.Exists(v => v.Contains("chunked", StringComparison.OrdinalIgnoreCase)))
            {
                while (true)
                {
                    var sizeLine = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    var size = int.Parse(sizeLine.Split(';')[0].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    if (size == 0)
                    {
                        while ((await ReadLineAsync(cancellationToken).ConfigureAwait(false)).Length > 0)
                        {
                            // Trailers, then the empty line.
                        }

                        return;
                    }

                    await SkipExactlyAsync(size, cancellationToken).ConfigureAwait(false);
                    await ReadLineAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            var length = headers.TryGetValue("Content-Length", out var cl)
                ? int.Parse(cl[0], CultureInfo.InvariantCulture)
                : 0;
            await SkipExactlyAsync(length, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> ReadLineAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                for (var i = 0; i + 1 < _pending.Count; i++)
                {
                    if (_pending[i] == '\r' && _pending[i + 1] == '\n')
                    {
                        var line = Encoding.ASCII.GetString([.. _pending.Take(i)]);
                        _pending.RemoveRange(0, i + 2);
                        return line;
                    }
                }

                if (!await FillAsync(cancellationToken).ConfigureAwait(false))
                    throw new InvalidDataException("connection closed mid-body");
            }
        }

        private async Task SkipExactlyAsync(int length, CancellationToken cancellationToken)
        {
            while (_pending.Count < length)
            {
                if (!await FillAsync(cancellationToken).ConfigureAwait(false))
                    throw new InvalidDataException("connection closed mid-body");
            }

            _pending.RemoveRange(0, length);
        }

        private async Task<bool> FillAsync(CancellationToken cancellationToken)
        {
            var read = await stream.ReadAsync(_buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return false;
            _pending.AddRange(_buffer.AsSpan(0, read).ToArray());
            return true;
        }

        private int IndexOfHeadEnd()
        {
            for (var i = 0; i + 3 < _pending.Count; i++)
            {
                if (_pending[i] == '\r' && _pending[i + 1] == '\n' && _pending[i + 2] == '\r' && _pending[i + 3] == '\n')
                    return i;
            }

            return -1;
        }
    }
}
