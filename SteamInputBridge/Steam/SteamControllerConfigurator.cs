using System;
using System.Globalization;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SteamInputBridge.Steam;

internal static class SteamControllerConfigurator
{
    private const string SharedContextTitle = "SharedJSContext";
    private const int RequestId = 1;
    private static readonly Uri CefTabsEndpoint = new("http://127.0.0.1:8080/json");

    public static async ValueTask OpenAsync(uint appId, CancellationToken cancellationToken)
    {
        string webSocketUrl = await FindSharedContextWebSocketAsync(cancellationToken).ConfigureAwait(false);

        using ClientWebSocket socket = new();
        await socket.ConnectAsync(new Uri(webSocketUrl), cancellationToken).ConfigureAwait(false);
        await SendEvaluateRequestAsync(socket, appId, cancellationToken).ConfigureAwait(false);
        await ReadEvaluateResponseAsync(socket, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> FindSharedContextWebSocketAsync(CancellationToken cancellationToken)
    {
        using HttpClient http = new()
        {
            Timeout = TimeSpan.FromSeconds(2),
        };

        using HttpResponseMessage response = await http.GetAsync(CefTabsEndpoint, cancellationToken)
            .ConfigureAwait(false);
        _ = response.EnsureSuccessStatusCode();

        using System.IO.Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        foreach (JsonElement tab in document.RootElement.EnumerateArray())
        {
            if (!tab.TryGetProperty("title", out JsonElement title) ||
                !tab.TryGetProperty("webSocketDebuggerUrl", out JsonElement url) ||
                !string.Equals(title.GetString(), SharedContextTitle, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(url.GetString()))
            {
                continue;
            }

            return url.GetString()!;
        }

        throw new InvalidOperationException(
            "Steam CEF remote debugging is reachable, but SharedJSContext was not found.");
    }

    private static async Task SendEvaluateRequestAsync(
        ClientWebSocket socket,
        uint appId,
        CancellationToken cancellationToken)
    {
        string expression =
            "SteamClient.Apps.ShowControllerConfigurator(" +
            appId.ToString(CultureInfo.InvariantCulture) +
            ");";
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id = RequestId,
            method = "Runtime.evaluate",
            @params = new
            {
                expression,
                returnByValue = true,
                awaitPromise = true,
            },
        });

        await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ReadEvaluateResponseAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[16 * 1024];
        StringBuilder builder = new();

        while (true)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("Steam closed the CEF debug socket before responding.");
            }

            _ = builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage)
            {
                continue;
            }

            using JsonDocument document = JsonDocument.Parse(builder.ToString());
            if (!ResponseMatchesRequest(document.RootElement))
            {
                _ = builder.Clear();
                continue;
            }

            ThrowIfProtocolError(document.RootElement);
            return;
        }
    }

    private static bool ResponseMatchesRequest(JsonElement root)
    {
        return root.TryGetProperty("id", out JsonElement id) &&
            id.ValueKind == JsonValueKind.Number &&
            id.TryGetInt32(out int value) &&
            value == RequestId;
    }

    private static void ThrowIfProtocolError(JsonElement root)
    {
        if (root.TryGetProperty("error", out JsonElement error))
        {
            throw new InvalidOperationException("Steam CEF rejected the request: " + error);
        }

        if (root.TryGetProperty("result", out JsonElement result) &&
            result.TryGetProperty("exceptionDetails", out JsonElement exception))
        {
            throw new InvalidOperationException("Steam configurator script failed: " + exception);
        }
    }

}
