# ClodTelemetryBridge

A small Windows telemetry bridge for IL-2 Sturmovik: Cliffs of Dover / Blitz.

This project reads live telemetry directly from the game's CLODDeviceLink shared memory block and forwards it to either:

- Sim Racing Studio (SRS)
- SimHub

--------------------------------------------------

## What it does

Cliffs of Dover does not provide a simple modern built-in UDP telemetry stream for motion software.

This tool reads live telemetry from the game's shared memory block, CLODDeviceLink, and converts it into formats understood by external telemetry and motion software.

--------------------------------------------------

## Current output modes

The bridge currently supports:

- SRS
- SimHub

The included batch launcher lets the user choose which mode to start.

--------------------------------------------------

## Current telemetry mapping

### Confirmed live Cliffs of Dover slots

The bridge currently uses these known working CLODDeviceLink values:

- 840 = Z_Orientation[0] = yaw / heading-like
- 841 = Z_Orientation[1] = pitch
- 842 = Z_Orientation[2] = roll
- 1769 = I_DirectionIndicator[-1] = heading-like 0..360
- 1609 = I_VelocityIAS[-1] = IAS / speed-like
- 1481 = I_EngineRPM[1] = engine RPM-like

### Output mapping

#### SRS

- Pitch = slot 841
- Roll = slot 842
- Yaw = selected heading (1769 preferred, fallback 840)
- Speed = slot 1609
- RPM = slot 1481

#### SimHub

- YawDegrees = selected heading (1769 preferred, fallback 840)
- PitchDegrees = slot 841
- RollDegrees = slot 842

--------------------------------------------------

## Requirements

- Windows
- IL-2 Sturmovik: Cliffs of Dover / Blitz
- .NET 8 runtime if using a framework-dependent build
  or no runtime requirement if using a self-contained published build

--------------------------------------------------

## How to use

### Recommended method

Run:

Launch_ClodTelemetryBridge.bat

You will be asked to choose:

- 1 = SRS
- 2 = SimHub
- 3 = Exit

The batch file then launches the bridge with the correct mode automatically.

### Manual launch

You can also run the bridge directly:

ClodTelemetryBridge.exe -srs

or

ClodTelemetryBridge.exe -simhub

--------------------------------------------------

## Important launch order

### Sim Racing Studio (important)

SRS can auto-detect Cliffs of Dover and may switch to its own built-in game path, which can conflict with the generic API bridge.

Recommended launch order for SRS:

1. Start Cliffs of Dover
2. Run Launch_ClodTelemetryBridge.bat
3. Choose SRS
4. Start Sim Racing Studio last

If SRS is started too early, it may detect Launcher64.exe and use a different internal telemetry mode instead of the API stream.

### SimHub

For SimHub, launch order is generally less sensitive.

Typical order:

1. Start SimHub
2. Run Launch_ClodTelemetryBridge.bat
3. Choose SimHub
4. Start Cliffs of Dover

--------------------------------------------------

## Files to distribute

For testers, distribute the full output folder, typically including:

- ClodTelemetryBridge.exe
- ClodTelemetryBridge.dll
- ClodTelemetryBridge.runtimeconfig.json
- ClodTelemetryBridge.deps.json
- Launch_ClodTelemetryBridge.bat

If you are publishing for non-technical testers, a self-contained publish is recommended.

--------------------------------------------------

## SimHub setup

This project expects a matching SimHub External Sim Definition for Cliffs of Dover.

The current SimHub packet contains:

- YawDegrees
- PitchDegrees
- RollDegrees

If your SimHub definition uses a different layout, regenerate the definition and update the bridge accordingly.

--------------------------------------------------

## SRS notes

SRS support in this project uses the generic SRS API UDP packet.

If SRS shows the game as detected but telemetry is not updating, it is often because SRS has switched to a built-in Cliffs of Dover detection path instead of staying on the API stream. In that case, follow the launch order above.

--------------------------------------------------

## Building

Open the project in Visual Studio 2022 and build as a .NET 8 Windows console app.

Recommended target framework:

<TargetFramework>net8.0-windows</TargetFramework>

--------------------------------------------------

## Known limitations

- Some Cliffs values are still under investigation
- IAS/speed units may vary by aircraft
- RPM and some instrument values may depend on aircraft type
- SRS built-in CloD detection can conflict with the generic API stream
- SimHub currently uses orientation only unless expanded further

--------------------------------------------------

## Planned improvements

- More telemetry fields
- Better aircraft/state detection
- Optional config file
- Improved SRS/API conflict handling
- Expanded SimHub support

--------------------------------------------------

## Credits / research notes

This project is based on ongoing investigation of the CLODDeviceLink shared memory interface, including old Team Fusion / ATAG forum references and newer validation of working telemetry slots in current builds.

--------------------------------------------------

## Disclaimer

This is an unofficial community telemetry bridge.

Use at your own risk and test carefully before relying on it for motion hardware.
