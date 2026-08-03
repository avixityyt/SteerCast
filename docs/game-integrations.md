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
the game's `extradata="3"` UDP packet and derives vehicle load from lateral G,
longitudinal G, and suspension motion. It does not represent wheel resistance.

Close the game and edit:

`Documents\My Games\DiRT Rally 2.0\hardwaresettings\hardware_settings_config.xml`

Set:

```xml
<udp enabled="true" extradata="3" ip="127.0.0.1" port="20777" delay="1" />
```

Use the equivalent VR configuration file when playing in VR. Then restart the
game and begin a stage. If another telemetry program owns port `20777`, it must
forward packets or use a different port before SteerCast can receive them.
