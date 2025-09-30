using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Http;
using NzbWebDAV.Extensions;
using NzbWebDAV.Utils;
using Serilog;

namespace NzbWebDAV.Websocket;

public class WebsocketManager
{
    private readonly HashSet<WebSocket> _authenticatedSockets = [];
    private readonly Dictionary<WebsocketTopic, string> _lastMessage = new();

    public async Task HandleRoute(HttpContext context)
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            if (!await Authenticate(webSocket))
            {
                Log.Warning($"Closing unauthenticated websocket connection from {context.Connection.RemoteIpAddress}");
                await CloseUnauthorizedConnection(webSocket);
                return;
            }

            // mark the socket as authenticated
            lock (_authenticatedSockets)
                _authenticatedSockets.Add(webSocket);

            // send current state for all topics
            List<KeyValuePair<WebsocketTopic, string>>? lastMessage;
            lock (_lastMessage) lastMessage = _lastMessage.ToList();
            foreach (var message in lastMessage)
                if (message.Key.Type == WebsocketTopic.TopicType.State)
                    await SendMessage(webSocket, message.Key, message.Value);

            // wait for the socket to disconnect
            await WaitForDisconnected(webSocket);
            lock (_authenticatedSockets)
                _authenticatedSockets.Remove(webSocket);
        }
        else
        {
            context.Response.StatusCode = 400;
        }
    }

    /// <summary>
    /// Send a message to all authenticated websockets.
    /// </summary>
    /// <param name="topic">The topic of the message to send</param>
    /// <param name="message">The message to send</param>
    public Task SendMessage(WebsocketTopic topic, string message)
    {
        lock (_lastMessage) _lastMessage[topic] = message;
        List<WebSocket>? authenticatedSockets;
        lock (_authenticatedSockets) authenticatedSockets = _authenticatedSockets.ToList();
        var topicMessage = new TopicMessage(topic, message);
        var bytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(topicMessage.ToJson()));
        return Task.WhenAll(authenticatedSockets.Select(x => SendMessage(x, bytes)));
    }

    /// <summary>
    /// Ensure a websocket sends a valid api key.
    /// </summary>
    /// <param name="socket">The websocket to authenticate.</param>
    /// <returns>True if authenticated, False otherwise.</returns>
    private static async Task<bool> Authenticate(WebSocket socket)
    {
        var apiKey = await ReceiveAuthToken(socket);
        return apiKey == EnvironmentUtil.GetVariable("FRONTEND_BACKEND_API_KEY");
    }

    /// <summary>
    /// Ignore all messages from the websocket and
    /// wait for it to disconnect.
    /// </summary>
    /// <param name="socket">The websocket to wait for disconnect.</param>
    private static async Task WaitForDisconnected(WebSocket socket)
    {
        try
        {
            var buffer = new byte[1024];
            WebSocketReceiveResult? result = null;
            while (result is not { CloseStatus: not null })
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), default);
            await socket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, default);
        }
        catch (Exception e)
        {
            Log.Warning(e.Message);
        }
    }

    /// <summary>
    /// Send a message to a connected websocket.
    /// </summary>
    /// <param name="socket">The websocket to send the message to.</param>
    /// <param name="topic">The topic of the message to send</param>
    /// <param name="message">The message to send</param>
    private static async Task SendMessage(WebSocket socket, WebsocketTopic topic, string message)
    {
        var topicMessage = new TopicMessage(topic, message);
        var bytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(topicMessage.ToJson()));
        await SendMessage(socket, bytes);
    }

    /// <summary>
    /// Send a message to a connected websocket.
    /// </summary>
    /// <param name="socket">The websocket to send the message to.</param>
    /// <param name="message">The message to send.</param>
    private static async Task SendMessage(WebSocket socket, ArraySegment<byte> message)
    {
        try
        {
            await socket.SendAsync(message, WebSocketMessageType.Text, true, default);
        }
        catch (Exception e)
        {
            Log.Debug($"Failed to send message to websocket. {e.Message}");
        }
    }

    /// <summary>
    /// Receive an authentication token from a connected websocket.
    /// With timeout after five seconds.
    /// </summary>
    /// <param name="socket">The websocket to receive from.</param>
    /// <returns>The authentication token. Or null if none provided.</returns>
    private static async Task<string?> ReceiveAuthToken(WebSocket socket)
    {
        try
        {
            var buffer = new byte[1024];
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
            return result.MessageType == WebSocketMessageType.Text
                ? Encoding.UTF8.GetString(buffer, 0, result.Count)
                : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Close a websocket connection as unauthorized.
    /// </summary>
    /// <param name="socket">The websocket whose connection to close.</param>
    private static async Task CloseUnauthorizedConnection(WebSocket socket)
    {
        if (socket.State == WebSocketState.Open)
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Unauthorized", CancellationToken.None);
    }

    private sealed class TopicMessage(WebsocketTopic topic, string message)
    {
        public string Topic { get; } = topic.Name;
        public string Message { get; } = message;
    }
}