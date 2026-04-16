@echo off
title Clod Telemetry Bridge Launcher

echo.
echo ==========================================
echo      Clod Telemetry Bridge Launcher
echo ==========================================
echo.
echo Choose telemetry mode:
echo.
echo   1. SRS
echo   2. SimHub
echo   3. Exit
echo.

set /p choice=Enter choice (1/2/3): 

if "%choice%"=="1" goto srs
if "%choice%"=="2" goto simhub
if "%choice%"=="3" goto end

echo.
echo Invalid choice.
pause
goto end

:srs
echo.
echo Launching ClodTelemetryBridge in SRS mode...
start "" "ClodTelemetryBridge.exe" -srs
goto end

:simhub
echo.
echo Launching ClodTelemetryBridge in SimHub mode...
start "" "ClodTelemetryBridge.exe" -simhub
goto end

:end
exit