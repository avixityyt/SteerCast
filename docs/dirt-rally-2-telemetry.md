# DiRT Rally 2.0 derived-load telemetry

SteerCast can passively receive the game’s UDP telemetry and calculate a
derived vehicle-load signal for the setup UI. It does not read, control, or
approximate the force feedback sent to the wheel.

## Enable telemetry

1. Start DiRT Rally 2.0 once, then close it.
2. Open `Documents\My Games\DiRT Rally 2.0\hardwaresettings\hardware_settings_config.xml`.
3. Change the UDP entry to:

```xml
<udp enabled="true" extradata="3" ip="127.0.0.1" port="20777" delay="1" />
```

4. If you use VR, make the same change in `hardware_settings_config_vr.xml`.
5. Start a stage. The Controller panel should report `Receiving telemetry`.

The listener accepts only localhost packets and uses the documented
`extradata="3"` packet layout. It derives a display value from lateral and
longitudinal G-force plus suspension motion, smooths it, and suppresses values
below 5%. The result is explicitly labelled **Derived load** because it is not
the steering wheel’s actual force feedback.

## Other telemetry software

DiRT Rally 2.0 sends UDP telemetry to one configured target. If another tool
already uses port `20777`, configure that tool to forward packets to SteerCast,
or change the game and SteerCast to a matching unused port in a future adapter
configuration update. Do not bind SteerCast to a LAN address.
