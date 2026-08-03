using SteerCast.Core.Models;

namespace SteerCast.App.Services;

/// <summary>
/// Passive data explicitly exported by a game. Implementations must not open
/// game processes, inject code, control wheel hardware, or alter controller routing.
/// </summary>
public interface IGameTelemetrySource : IDisposable
{
    GameTelemetryReading Reading { get; }
}
