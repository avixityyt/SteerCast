using System.Collections.Concurrent;
using System.Text.Json;
using SteerCast.Core.Models;

namespace SteerCast.App.Services;

public sealed class ProfileStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, OverlayProfile> _cache = new(StringComparer.Ordinal);
    private readonly string _directory;
    private volatile bool _loaded;

    public ProfileStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteerCast",
            "profiles");
    }

    public async Task<IReadOnlyList<OverlayProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _cache.Values.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<OverlayProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!IsValidId(id))
        {
            return null;
        }

        await EnsureLoadedAsync(cancellationToken);
        return _cache.TryGetValue(id, out var profile) ? profile : null;
    }

    public async Task<OverlayProfile> SaveAsync(OverlayProfile profile, CancellationToken cancellationToken = default)
    {
        if (!IsValidId(profile.Id))
        {
            throw new ArgumentException("Profile IDs may contain lowercase letters, numbers, and hyphens only.", nameof(profile));
        }

        var normalized = Normalize(profile);

        await EnsureLoadedAsync(cancellationToken);
        var target = GetPath(normalized.Id);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await WriteFileAsync(target, normalized, cancellationToken);
            _cache[normalized.Id] = normalized;
        }
        finally
        {
            _gate.Release();
        }

        return normalized;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        if (!IsValidId(id) || string.Equals(id, "default", StringComparison.Ordinal))
        {
            return false;
        }

        await EnsureLoadedAsync(CancellationToken.None);
        var path = GetPath(id);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        _cache.TryRemove(id, out _);
        return true;
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_loaded)
            {
                return;
            }

            Directory.CreateDirectory(_directory);
            foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
            {
                var profile = await ReadAsync(file, cancellationToken);
                if (profile is not null)
                {
                    await WriteFileAsync(file, profile, cancellationToken);
                    _cache[profile.Id] = profile;
                }
            }

            if (!_cache.ContainsKey("default"))
            {
                var defaultProfile = new OverlayProfile { Id = "default", Name = "Default Overlay" };
                await WriteFileAsync(GetPath("default"), defaultProfile, cancellationToken);
                _cache[defaultProfile.Id] = defaultProfile;
            }

            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task WriteFileAsync(string target, OverlayProfile profile, CancellationToken cancellationToken)
    {
        var temporary = target + ".tmp";
        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, profile, AppJsonContext.Default.OverlayProfile, cancellationToken);
        }

        File.Move(temporary, target, true);
    }

    private async Task<OverlayProfile?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var profile = await JsonSerializer.DeserializeAsync(stream, AppJsonContext.Default.OverlayProfile, cancellationToken);
            return profile is null ? null : Normalize(profile);
        }
        catch (JsonException)
        {
            var backup = path + $".corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            File.Move(path, backup, true);
            return null;
        }
    }

    private string GetPath(string id) => Path.Combine(_directory, $"{id}.json");

    private static OverlayProfile Normalize(OverlayProfile profile)
    {
        var width = Math.Clamp(profile.Width, 320, 3840);
        var height = Math.Clamp(profile.Height, 240, 2160);
        var defaultLayout = DefaultLayout(width, height, profile.HandbrakeEnabled);
        var resetLegacyLayout = profile.SchemaVersion < OverlayProfile.CurrentSchemaVersion;
        var layout = resetLegacyLayout ? defaultLayout : profile.Layout ?? defaultLayout;
        var theme = NormalizeTheme(profile.Theme);
        var mapping = profile.Mapping ?? new InputMapping();

        return profile with
        {
            SchemaVersion = OverlayProfile.CurrentSchemaVersion,
            Name = string.IsNullOrWhiteSpace(profile.Name) ? "Overlay" : profile.Name.Trim(),
            DeviceId = NormalizeDeviceId(profile.DeviceId),
            HandbrakeDeviceId = NormalizeDeviceId(profile.HandbrakeDeviceId),
            Width = width,
            Height = height,
            FramesPerSecond = profile.FramesPerSecond >= 120 ? 120 : 60,
            WheelRotationDegrees = Math.Clamp(profile.WheelRotationDegrees, 180, 1440),
            Mapping = mapping,
            Layout = layout with
            {
                Wheel = NormalizeElement(layout.Wheel, defaultLayout.Wheel, width, height, 420, 430),
                Pedals = NormalizeElement(layout.Pedals, defaultLayout.Pedals, width, height, 360, 205),
                Gear = NormalizeElement(layout.Gear, defaultLayout.Gear, width, height, 220, 250),
                Handbrake = NormalizeElement(layout.Handbrake, defaultLayout.Handbrake, width, height, 150, 210),
                Buttons = NormalizeElement(layout.Buttons, defaultLayout.Buttons, width, height, 340, 44)
            },
            Theme = theme
        };
    }

    private static OverlayLayout DefaultLayout(int width, int height, bool handbrakeEnabled)
    {
        var wide = width >= 1000;
        return new OverlayLayout
        {
            Wheel = wide
                ? new OverlayElement { X = Math.Round((width - 420) / 2.0), Y = Math.Max(48, height - 540) }
                : new OverlayElement { X = Math.Max(20, Math.Round((width - 420) / 2.0)), Y = 56 },
            Pedals = wide
                ? new OverlayElement { X = 55, Y = Math.Max(70, height - 220) }
                : new OverlayElement { X = 35, Y = Math.Max(285, height - 220) },
            Gear = wide
                ? new OverlayElement { X = Math.Max(0, width - 360), Y = 92 }
                : new OverlayElement { X = Math.Max(0, width - 235), Y = 92 },
            Handbrake = wide
                ? new OverlayElement { X = Math.Max(0, width - 180), Y = Math.Max(80, height - 285), Visible = handbrakeEnabled }
                : new OverlayElement { X = 545, Y = 330, Visible = handbrakeEnabled },
            Buttons = wide
                ? new OverlayElement { X = 70, Y = Math.Max(0, height - 65), Visible = false }
                : new OverlayElement { X = 35, Y = Math.Max(0, height - 65), Visible = false }
        };
    }

    private static OverlayElement NormalizeElement(
        OverlayElement? element,
        OverlayElement fallback,
        int canvasWidth,
        int canvasHeight,
        double elementWidth,
        double elementHeight)
    {
        if (element is null || !double.IsFinite(element.X) || !double.IsFinite(element.Y) || !double.IsFinite(element.Scale))
        {
            return fallback;
        }

        var scale = Math.Clamp(element.Scale, 0.4, 2);
        var maxX = canvasWidth - elementWidth * scale;
        var maxY = canvasHeight - elementHeight * scale;
        if (element.X < 0 || element.Y < 0 || element.X > maxX || element.Y > maxY)
        {
            return fallback with { Visible = element.Visible };
        }

        return element with { Scale = scale };
    }

    private static OverlayTheme NormalizeTheme(OverlayTheme? theme)
    {
        if (theme is null)
        {
            return new OverlayTheme();
        }

        if (string.Equals(theme.Name, "G920 Inspired", StringComparison.Ordinal)
            || (string.Equals(theme.Accent, "#28a9ff", StringComparison.OrdinalIgnoreCase)
                && string.Equals(theme.Brake, "#ff4655", StringComparison.OrdinalIgnoreCase)))
        {
            return new OverlayTheme();
        }

        return theme;
    }

    private static string? NormalizeDeviceId(string? deviceId)
    {
        var normalized = deviceId?.Trim().Trim('\0');
        if (normalized?.StartsWith("{wgi/nrid/", StringComparison.OrdinalIgnoreCase) == true)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static bool IsValidId(string id) =>
        !string.IsNullOrWhiteSpace(id)
        && id.Length <= 64
        && id.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
}
