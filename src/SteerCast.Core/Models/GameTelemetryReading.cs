namespace SteerCast.Core.Models;

/// <summary>
/// A display-oriented signal calculated from telemetry a game explicitly
/// exports. It is not a measurement of force feedback or wheel motor torque.
/// </summary>
public sealed record GameTelemetryReading(
    string Source,
    string GameName,
    bool Available,
    bool Active,
    double Strength,
    int Direction,
    long Timestamp,
    int Port,
    string Status);
