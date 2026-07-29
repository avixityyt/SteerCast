# Optional Logitech telemetry adapter

SteerCast's normal input path is Windows Gaming Input. It does not require G HUB,
the Logitech SDK, or any vendor DLL.

The optional adapter boundary is intentionally separate. An adapter may report
Logitech SDK force/torque telemetry when the user has the compatible SDK
installed, but it must not seize the wheel, stop effects, or replace the normal
input path. The overlay continues to work when the adapter is missing, blocked,
or incompatible.

The current application reports adapter state at:

```text
GET /api/integrations/logitech
```

The initial status is `available: false` and `source: "none"`. Logitech SDK
DLLs, headers, and binaries are deliberately not bundled in this repository.
The adapter package must be distributed separately only after Logitech
redistribution terms are confirmed.

## First-run UX contract

The setup page should present this as an optional enhancement:

1. Detect the wheel through Windows input.
2. Detect whether the optional adapter is available.
3. Show `FFB telemetry available` or `FFB telemetry unavailable` without
   blocking calibration or OBS setup.
4. Use the standard input overlay in either case.

## Rollback checkpoint

If the adapter is unreliable, remove the adapter implementation and retain the
`IForceFeedbackAdapter` boundary, `NullForceFeedbackAdapter`, and the normal
Windows input path. This returns SteerCast to its pre-adapter behavior without
changing profiles or overlay URLs.
