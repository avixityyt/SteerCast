namespace SteerCast.Core.Models;

/// <summary>Configuration for one optional, passive game telemetry adapter.</summary>
public sealed record GameIntegrationSettings(
    bool Enabled = false,
    string GameId = "dirt-rally-2",
    bool ShowOnOverlay = true);

public sealed record GameIntegrationDescriptor(
    string Id,
    string Name,
    string SignalKind,
    string SignalLabel,
    int Port,
    string Summary);

/// <summary>
/// Display-oriented data exported by a game. SignalKind distinguishes actual
/// FFB, steering torque, and derived telemetry.
/// </summary>
public sealed record GameTelemetryReading(
    string Source,
    string GameName,
    string SignalKind,
    string SignalLabel,
    bool Available,
    bool Active,
    double Strength,
    int Direction,
    long Timestamp,
    int Port,
    string Status);

public sealed record GameSetupState(
    string Status,
    bool ConfigFound,
    bool Configured,
    bool CanConfigure,
    string[] ConfigPaths,
    string[] BackupPaths,
    string Message);

public sealed record GameIntegrationSnapshot(
    GameIntegrationSettings Settings,
    GameIntegrationDescriptor[] Games,
    GameTelemetryReading Telemetry,
    GameSetupState Setup);
