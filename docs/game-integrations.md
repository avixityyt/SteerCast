# Game integrations

SteerCast keeps wheel input capture independent from optional game adapters.
Adapters may only consume telemetry that a game explicitly exports through a
documented local interface such as UDP, shared memory, or a supported plugin.
They must not inject into a game, inspect private process memory, emulate a
controller, or send commands to wheel hardware.

Every adapter declares its signal kind:

- `force-feedback`: the game explicitly exports its final FFB command.
- `steering-torque`: steering or rack torque exported by the game.
- `derived-telemetry`: a display-oriented estimate calculated from vehicle data.

The setup UI and overlay must use that declaration. Derived telemetry must never
be labelled force feedback.

## DiRT Rally 2.0

The first adapter listens on `127.0.0.1:20777` only while enabled. It consumes
the game's `extradata="3"` UDP packet and derives steering demand from vehicle
slip angle, yaw rate, speed, G-load, and suspension motion. It does not
represent wheel resistance.

The Games panel detects the normal and VR configuration files. Its Quick setup
action creates a one-time `.steercast-backup` beside each file, changes only the
UDP attributes, replaces the file atomically, and enables the local listener.
The user must close the game before running automatic setup. The UI confirms
completion only after a telemetry packet arrives during a stage.

### Derived steering interaction

When DiRT telemetry is available, the overlay learns steering/yaw sign polarity
during low-slip grip driving. One `LOAD` pill shows demand from vehicle physics.
During a slide, holding the wheel opposite the calibrated yaw direction adds a
directional amber tension glow and a subtle deflection to the wheel image. The
effect remains visible while the wheel angle is steady because slip and yaw are
still active. It cannot distinguish motor movement from the driver's hands and
must not be labelled force feedback.

The Games panel can record a 30-second local tuning sample. Capture subscribes
to the existing input stream, stores at most 20 samples per second, and performs
no additional device or game polling. Files are written to
`%LOCALAPPDATA%\SteerCast\captures` and contain steering, pedal, derived load,
speed, slip-angle, and yaw-rate values. Capture files are local diagnostics and
must not be committed.

If automatic setup is unavailable, close the game and edit:

`Documents\My Games\DiRT Rally 2.0\hardwaresettings\hardware_settings_config.xml`

Set:

```xml
<udp enabled="true" extradata="3" ip="127.0.0.1" port="20777" delay="1" />
```

Use the equivalent VR configuration file when playing in VR. Then restart the
game and begin a stage. If another telemetry program owns port `20777`, it must
forward packets or use a different port before SteerCast can receive them.
