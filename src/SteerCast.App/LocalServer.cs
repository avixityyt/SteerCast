using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SteerCast.App.Services;
using SteerCast.Core;
using SteerCast.Core.Models;

namespace SteerCast.App;

public sealed class LocalServer(
    int port,
    ProfileStore profileStore,
    IWheelInputSource inputSource,
    InputBroadcaster broadcaster,
    GameIntegrationManager gameIntegrations) : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stopping = new();
    private Task? _worker;
    private int _stopped;

    public string BaseUrl { get; } = $"http://127.0.0.1:{port}/";

    public void Start()
    {
        _listener.Prefixes.Add(BaseUrl);
        _listener.Start();
        broadcaster.Start();
        _worker = AcceptLoopAsync(_stopping.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            _ = Task.Run(() => HandleAsync(context, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            var request = context.Request;
            var path = request.Url?.AbsolutePath.TrimEnd('/') ?? string.Empty;
            if (path.Length == 0)
            {
                Redirect(context, "/setup");
                return;
            }

            if (path == "/setup" && request.HttpMethod == "GET")
            {
                await SendHtmlAsync(context, "setup.html", null, cancellationToken);
                return;
            }

            if (path.StartsWith("/overlay/", StringComparison.Ordinal) && request.HttpMethod == "GET")
            {
                var id = Uri.UnescapeDataString(path["/overlay/".Length..]);
                await SendHtmlAsync(context, "overlay.html", new("__PROFILE_ID__", id), cancellationToken);
                return;
            }

            if (path.StartsWith("/ws/input/", StringComparison.Ordinal))
            {
                await HandleWebSocketAsync(context, Uri.UnescapeDataString(path["/ws/input/".Length..]), cancellationToken);
                return;
            }

            if (path.StartsWith("/api/", StringComparison.Ordinal))
            {
                await HandleApiAsync(context, path, cancellationToken);
                return;
            }

            if (path.StartsWith("/user-assets/", StringComparison.Ordinal))
            {
                await SendUserAssetAsync(context, path["/user-assets/".Length..], cancellationToken);
                return;
            }

            await SendStaticAsync(context, path, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            context.Response.Abort();
        }
        catch (Exception exception)
        {
            if (context.Response.OutputStream.CanWrite)
            {
                await SendJsonAsync(context, new ErrorResponse(exception.Message), AppJsonContext.Default.ErrorResponse, 500, cancellationToken);
            }
        }
        finally
        {
            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.Close();
            }
        }
    }

    private async Task HandleApiAsync(HttpListenerContext context, string path, CancellationToken cancellationToken)
    {
        var method = context.Request.HttpMethod;
        if (method == "GET" && path == "/api/health")
        {
            await SendJsonAsync(context, new HealthResponse(
                "ok",
                typeof(LocalServer).Assembly.GetName().Version?.ToString() ?? "dev",
                broadcaster.ClientCount,
                inputSource.GetDevices().Count), AppJsonContext.Default.HealthResponse, 200, cancellationToken);
            return;
        }

        if (method == "GET" && path == "/api/devices")
        {
            await SendJsonAsync(context, inputSource.GetDevices().ToArray(), AppJsonContext.Default.DeviceDescriptorArray, 200, cancellationToken);
            return;
        }

        if (method == "GET" && path == "/api/game-integrations")
        {
            await SendJsonAsync(
                context,
                gameIntegrations.Snapshot,
                AppJsonContext.Default.GameIntegrationSnapshot,
                200,
                cancellationToken);
            return;
        }

        if (method == "PUT" && path == "/api/game-integrations")
        {
            var settings = await JsonSerializer.DeserializeAsync(
                context.Request.InputStream,
                AppJsonContext.Default.GameIntegrationSettings,
                cancellationToken);
            if (settings is null)
            {
                await SendJsonAsync(
                    context,
                    new ErrorResponse("Game integration settings are required."),
                    AppJsonContext.Default.ErrorResponse,
                    400,
                    cancellationToken);
                return;
            }

            try
            {
                var snapshot = await gameIntegrations.UpdateAsync(settings, cancellationToken);
                await SendJsonAsync(
                    context,
                    snapshot,
                    AppJsonContext.Default.GameIntegrationSnapshot,
                    200,
                    cancellationToken);
            }
            catch (ArgumentException exception)
            {
                await SendJsonAsync(
                    context,
                    new ErrorResponse(exception.Message),
                    AppJsonContext.Default.ErrorResponse,
                    400,
                    cancellationToken);
            }
            return;
        }

        if (method == "POST" && path == "/api/game-integrations/dirt-rally-2/configure")
        {
            try
            {
                await SendJsonAsync(
                    context,
                    gameIntegrations.ConfigureSelectedGame(),
                    AppJsonContext.Default.GameIntegrationSnapshot,
                    200,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                await SendJsonAsync(
                    context,
                    new ErrorResponse(exception.Message),
                    AppJsonContext.Default.ErrorResponse,
                    409,
                    cancellationToken);
            }
            return;
        }

        if (method == "POST" && path == "/api/devices/refresh")
        {
            inputSource.Refresh();
            await SendJsonAsync(context, inputSource.GetDevices().ToArray(), AppJsonContext.Default.DeviceDescriptorArray, 200, cancellationToken);
            return;
        }

        if (method == "GET" && path.StartsWith("/api/devices/", StringComparison.Ordinal) && path.EndsWith("/reading", StringComparison.Ordinal))
        {
            var id = Uri.UnescapeDataString(path["/api/devices/".Length..^"/reading".Length]);
            if (inputSource.GetRawReading(id) is { } reading)
            {
                await SendJsonAsync(context, reading, AppJsonContext.Default.RawDeviceReading, 200, cancellationToken);
            }
            else
            {
                context.Response.StatusCode = 404;
            }
            return;
        }

        if (method == "POST" && path.StartsWith("/api/calibration/", StringComparison.Ordinal))
        {
            var id = Uri.UnescapeDataString(path["/api/calibration/".Length..]);
            if (inputSource.GetDevices().All(device => device.Id != id))
            {
                context.Response.StatusCode = 404;
                return;
            }

            var request = await JsonSerializer.DeserializeAsync(
                context.Request.InputStream,
                AppJsonContext.Default.CalibrationRequest,
                cancellationToken);
            if (request is null)
            {
                await SendJsonAsync(context, new ErrorResponse("Calibration request is required."), AppJsonContext.Default.ErrorResponse, 400, cancellationToken);
                return;
            }

            try
            {
                await SendJsonAsync(context, CalibrationFactory.FromSamples(request), AppJsonContext.Default.AxisCalibration, 200, cancellationToken);
            }
            catch (ArgumentException exception)
            {
                await SendJsonAsync(context, new ErrorResponse(exception.Message), AppJsonContext.Default.ErrorResponse, 400, cancellationToken);
            }
            return;
        }

        if (method == "GET" && path == "/api/profiles")
        {
            await SendJsonAsync(context, (await profileStore.GetAllAsync(cancellationToken)).ToArray(), AppJsonContext.Default.OverlayProfileArray, 200, cancellationToken);
            return;
        }

        if (path.StartsWith("/api/profiles/", StringComparison.Ordinal))
        {
            var id = Uri.UnescapeDataString(path["/api/profiles/".Length..]);
            if (method == "GET")
            {
                if (await profileStore.GetAsync(id, cancellationToken) is { } profile)
                {
                    await SendJsonAsync(context, profile, AppJsonContext.Default.OverlayProfile, 200, cancellationToken);
                }
                else
                {
                    context.Response.StatusCode = 404;
                }
                return;
            }

            if (method == "PUT")
            {
                var profile = await JsonSerializer.DeserializeAsync(
                    context.Request.InputStream,
                    AppJsonContext.Default.OverlayProfile,
                    cancellationToken);
                if (profile is null || profile.Id != id)
                {
                    await SendJsonAsync(context, new ErrorResponse("The route and profile IDs must match."), AppJsonContext.Default.ErrorResponse, 400, cancellationToken);
                    return;
                }

                try
                {
                    var saved = await profileStore.SaveAsync(profile, cancellationToken);
                    await SendJsonAsync(context, saved, AppJsonContext.Default.OverlayProfile, 200, cancellationToken);
                }
                catch (ArgumentException exception)
                {
                    await SendJsonAsync(context, new ErrorResponse(exception.Message), AppJsonContext.Default.ErrorResponse, 400, cancellationToken);
                }
                return;
            }

            if (method == "DELETE")
            {
                context.Response.StatusCode = await profileStore.DeleteAsync(id) ? 204 : 400;
                return;
            }
        }

        context.Response.StatusCode = 404;
    }

    private async Task HandleWebSocketAsync(HttpListenerContext context, string profileId, CancellationToken cancellationToken)
    {
        if (!context.Request.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            return;
        }

        if (await profileStore.GetAsync(profileId, cancellationToken) is null)
        {
            context.Response.StatusCode = 404;
            return;
        }

        var webSocketContext = await context.AcceptWebSocketAsync(null);
        using var socket = webSocketContext.WebSocket;
        using var subscription = broadcaster.Subscribe(profileId);
        using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var receiveTask = ObserveCloseAsync(socket, connectionCancellation);
        try
        {
            await foreach (var frame in subscription.Reader.ReadAllAsync(connectionCancellation.Token))
            {
                if (socket.State != WebSocketState.Open)
                {
                    break;
                }

                await socket.SendAsync(
                    JsonSerializer.SerializeToUtf8Bytes(frame, AppJsonContext.Default.InputFrame),
                    WebSocketMessageType.Text,
                    true,
                    connectionCancellation.Token);
            }
        }
        catch (OperationCanceledException) when (receiveTask.IsCompleted || cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            await connectionCancellation.CancelAsync();
            try
            {
                await receiveTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private static async Task ObserveCloseAsync(WebSocket socket, CancellationTokenSource connectionCancellation)
    {
        var buffer = new byte[1];
        try
        {
            while (socket.State == WebSocketState.Open && !connectionCancellation.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, connectionCancellation.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            if (!connectionCancellation.IsCancellationRequested)
            {
                await connectionCancellation.CancelAsync();
            }
        }
    }

    private static async Task SendHtmlAsync(
        HttpListenerContext context,
        string fileName,
        KeyValuePair<string, string>? replacement,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", fileName);
        if (!File.Exists(path))
        {
            context.Response.StatusCode = 500;
            return;
        }

        var html = await File.ReadAllTextAsync(path, cancellationToken);
        if (replacement is { } value)
        {
            html = html.Replace(value.Key, value.Value, StringComparison.Ordinal);
        }

        await SendBytesAsync(context, Encoding.UTF8.GetBytes(html), "text/html; charset=utf-8", "no-cache", cancellationToken);
    }

    private static async Task SendStaticAsync(HttpListenerContext context, string requestPath, CancellationToken cancellationToken)
    {
        var relative = requestPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "wwwroot"));
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            context.Response.StatusCode = 404;
            return;
        }

        var contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".html" => "text/html; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".ico" => "image/x-icon",
            ".json" => "application/json; charset=utf-8",
            _ => "application/octet-stream"
        };
        await SendBytesAsync(
            context,
            await File.ReadAllBytesAsync(path, cancellationToken),
            contentType,
            "no-store, max-age=0",
            cancellationToken);
    }

    private static async Task SendUserAssetAsync(HttpListenerContext context, string requestPath, CancellationToken cancellationToken)
    {
        var relative = Uri.UnescapeDataString(requestPath).TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteerCast",
            "assets"));
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            context.Response.StatusCode = 404;
            return;
        }

        var contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => null
        };
        if (contentType is null)
        {
            context.Response.StatusCode = 404;
            return;
        }

        await SendBytesAsync(
            context,
            await File.ReadAllBytesAsync(path, cancellationToken),
            contentType,
            "no-store, max-age=0",
            cancellationToken);
    }

    private static async Task SendJsonAsync<T>(
        HttpListenerContext context,
        T value,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        int statusCode,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.OutputStream, value, typeInfo, cancellationToken);
    }

    private static async Task SendBytesAsync(
        HttpListenerContext context,
        byte[] bytes,
        string contentType,
        string cacheControl,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = 200;
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = bytes.Length;
        context.Response.Headers[HttpResponseHeader.CacheControl] = cacheControl;
        if (cacheControl.Contains("no-store", StringComparison.Ordinal))
        {
            context.Response.Headers[HttpResponseHeader.Pragma] = "no-cache";
            context.Response.Headers[HttpResponseHeader.Expires] = "0";
        }
        await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
    }

    private static void Redirect(HttpListenerContext context, string location)
    {
        context.Response.StatusCode = 302;
        context.Response.RedirectLocation = location;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        await _stopping.CancelAsync();
        _listener.Stop();
        if (_worker is not null)
        {
            try
            {
                await _worker;
            }
            catch (OperationCanceledException)
            {
            }
        }
        await broadcaster.StopAsync();
        _listener.Close();
        _stopping.Dispose();
    }
}

public sealed record HealthResponse(string Status, string Version, int Clients, int Devices);
public sealed record ErrorResponse(string Error);
