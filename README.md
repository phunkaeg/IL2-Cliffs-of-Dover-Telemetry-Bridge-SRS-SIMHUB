# ClodTelemetryBridge

A small Windows command-line bridge for **IL-2 Sturmovik: Cliffs of Dover** telemetry.

It reads telemetry directly from the game's `CLODDeviceLink` shared memory block and forwards it to either:

- **Sim Racing Studio (SRS)** via the SRS API UDP stream
- **SimHub** via a custom SimHub external-sim UDP definition

## What it does

This tool was built because Cliffs of Dover does not provide a modern built-in UDP telemetry stream for motion software.

Instead, it exposes a **memory-mapped telemetry block** called `CLODDeviceLink`. This project reads that live memory and converts it into formats understood by external motion/telemetry software.

## Current supported outputs

- `-srs`  
  Sends telemetry to **Sim Racing Studio**

- `-simhub`  
  Sends telemetry to **SimHub**

## Current telemetry mapping

### Confirmed Cliffs of Dover slots

The bridge currently uses these known live values from `CLODDeviceLink`:

- `840` = `Z_Orientation[0]` = yaw / heading-like
- `841` = `Z_Orientation[1]` = pitch
- `842` = `Z_Orientation[2]` = roll
- `1769` = `I_DirectionIndicator[-1]` = heading-like `0..360`
- `1609` = `I_VelocityIAS[-1]` = IAS / speed-like
- `1481` = `I_EngineRPM[1]` = engine RPM-like

### Output mapping

#### SRS
- Pitch = slot `841`
- Roll = slot `842`
- Yaw = selected heading (`1769` preferred, fallback `840`)
- Speed = slot `1609`
- RPM = slot `1481`

#### SimHub
- YawDegrees = selected heading (`1769` preferred, fallback `840`)
- PitchDegrees = slot `841`
- RollDegrees = slot `842`

## Requirements

- Windows
- **IL-2 Sturmovik: Cliffs of Dover / Blitz** running
- .NET 8 runtime if using a framework-dependent build  
  or no runtime needed if using a self-contained published build

## How to run

### Directly from the exe

```bat
ClodTelemetryBridge.exe -srs
