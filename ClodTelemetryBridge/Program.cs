using System;
using System.IO.MemoryMappedFiles;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace ClodTelemetryBridge
{
    internal enum ExportMode
    {
        Srs,
        SimHub
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SrsTelemetryPacket
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public char[] apiMode;

        public uint version;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 50)]
        public char[] game;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 50)]
        public char[] vehicleName;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 50)]
        public char[] location;

        public float speed;
        public float rpm;
        public float maxRpm;
        public int gear;

        public float pitch;
        public float roll;
        public float yaw;

        public float lateralVelocity;
        public float lateralAcceleration;
        public float verticalAcceleration;
        public float longitudinalAcceleration;

        public float suspensionTravelFrontLeft;
        public float suspensionTravelFrontRight;
        public float suspensionTravelRearLeft;
        public float suspensionTravelRearRight;

        public uint wheelTerrainFrontLeft;
        public uint wheelTerrainFrontRight;
        public uint wheelTerrainRearLeft;
        public uint wheelTerrainRearRight;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SimHubTelemetryPacket
    {
        public uint GameSignature;
        public uint TelemetrySignature;
        public ushort LayoutMajorVersion;
        public ushort LayoutMinorVersion;
        public ulong EmitterInstanceId;
        public byte PacketId;
        public ulong PacketsCounter;
        public byte IsSessionRunning;
        public byte IsSessionPaused;
        public ulong SessionId;
        public byte IsReplay;
        public byte IsUserInControl;
        public byte IsAIInControl;
        public byte IsSpectator;
        public double SessionTimeSeconds;
        public uint PhysicsDiscontinuityCounter;

        public float YawDegrees;
        public float PitchDegrees;
        public float RollDegrees;

        public static SimHubTelemetryPacket CreateDefault()
        {
            return new SimHubTelemetryPacket
            {
                GameSignature = 0x11142C9B,
                TelemetrySignature = 0xF1EA2F27,
                LayoutMajorVersion = 1,
                LayoutMinorVersion = 0
            };
        }
    }

    internal sealed class ClodReader : IDisposable
    {
        private const string MapName = "CLODDeviceLink";

        private MemoryMappedFile? _mmf;
        private MemoryMappedViewAccessor? _accessor;

        public bool Connected => _accessor != null;

        public bool TryConnect()
        {
            if (!OperatingSystem.IsWindows())
                return false;

            DisposeCurrent();

            try
            {
                _mmf = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
                _accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                return true;
            }
            catch
            {
                DisposeCurrent();
                return false;
            }
        }

        public double ReadSlot(int slotIndex)
        {
            if (_accessor == null)
                throw new InvalidOperationException("Not connected to CLODDeviceLink.");

            long offset = slotIndex * sizeof(double);
            return _accessor.ReadDouble(offset);
        }

        private void DisposeCurrent()
        {
            _accessor?.Dispose();
            _accessor = null;

            _mmf?.Dispose();
            _mmf = null;
        }

        public void Dispose()
        {
            DisposeCurrent();
        }
    }

    internal sealed class TelemetryState
    {
        public bool HasPrevious;
        public DateTime PrevTimeUtc;

        public double PrevPitch;
        public double PrevRoll;
        public double PrevHeading;

        public double SmoothedYawRate;
        public double SmoothedPitchRate;
        public double SmoothedRollRate;
    }

    internal sealed class FlightTelemetry
    {
        public bool IsValid;

        public double Heading840;
        public double Heading1769;
        public double HeadingSelected;

        public double PitchDeg;
        public double RollDeg;

        public double PitchRateDegPerSec;
        public double RollRateDegPerSec;
        public double YawRateDegPerSec;

        public double SpeedIAS;
        public double EngineRpm;
    }

    internal static class Program
    {
        private const string SrsComputerIp = "127.0.0.1";
        private const int SrsComputerPort = 33001;

        private const string SimHubHost = "127.0.0.1";
        private const int SimHubPort = 30777;

        private static readonly char[] ApiMode = "api".ToCharArray();
        private const uint ApiVersion = 102;

        private static readonly char[] Game = FixedChars("IL-2 Cliffs of Dover", 50);
        private static readonly char[] Vehicle = FixedChars("Unknown Aircraft", 50);
        private static readonly char[] Location = FixedChars("Unknown Location", 50);

        private const int LoopMs = 20;
        private const double SmoothingAlpha = 0.2;

        private static void Main(string[] args)
        {
            if (!TryParseArgs(args, out ExportMode mode))
            {
                PrintUsage();
                return;
            }

            Console.WriteLine($"ClodTelemetryBridge starting in {mode} mode...");
            Console.WriteLine("Press Ctrl+C to stop.");

            using ClodReader reader = new();
            using UdpClient udpClient = new();

            if (mode == ExportMode.SimHub)
            {
                udpClient.Connect(IPAddress.Parse(SimHubHost), SimHubPort);
            }

            while (!reader.TryConnect())
            {
                Console.WriteLine("Waiting for CLODDeviceLink...");
                Thread.Sleep(1000);
            }

            Console.WriteLine("Connected to CLODDeviceLink.");

            TelemetryState state = new();

            ulong simHubEmitterInstanceId = CreateRandomUInt64();
            ulong simHubSessionId = CreateRandomUInt64();
            ulong simHubPacketCounter = 0;
            DateTime simHubSessionStartUtc = DateTime.UtcNow;

            bool stopRequested = false;
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                stopRequested = true;
            };

            while (!stopRequested)
            {
                try
                {
                    FlightTelemetry telemetry = ReadTelemetry(reader, state);

                    if (!telemetry.IsValid)
                    {
                        Console.WriteLine("Telemetry invalid.");
                        Thread.Sleep(250);
                        continue;
                    }

                    if (mode == ExportMode.Srs)
                    {
                        SrsTelemetryPacket packet = BuildSrsPacket(telemetry);
                        byte[] bytes = StructToBytes(packet);
                        udpClient.Send(bytes, bytes.Length, SrsComputerIp, SrsComputerPort);
                    }
                    else
                    {
                        SimHubTelemetryPacket packet = BuildSimHubPacket(
                            telemetry,
                            simHubEmitterInstanceId,
                            simHubSessionId,
                            ref simHubPacketCounter,
                            simHubSessionStartUtc);

                        byte[] bytes = StructToBytes(packet);
                        udpClient.Send(bytes, bytes.Length);
                    }

                    Console.WriteLine(
                        "P:" + telemetry.PitchDeg.ToString("F2").PadLeft(8) +
                        " R:" + telemetry.RollDeg.ToString("F2").PadLeft(8) +
                        " H1769:" + telemetry.Heading1769.ToString("F2").PadLeft(8) +
                        " H840:" + telemetry.Heading840.ToString("F2").PadLeft(8) +
                        " HS:" + telemetry.HeadingSelected.ToString("F2").PadLeft(8) +
                        " IAS:" + telemetry.SpeedIAS.ToString("F2").PadLeft(8) +
                        " RPM:" + telemetry.EngineRpm.ToString("F2").PadLeft(8) +
                        " YR:" + telemetry.YawRateDegPerSec.ToString("F2").PadLeft(8));

                    Thread.Sleep(LoopMs);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Loop error: " + ex.Message);

                    while (!reader.TryConnect() && !stopRequested)
                    {
                        Console.WriteLine("Attempting reconnect to CLODDeviceLink...");
                        Thread.Sleep(1000);
                    }

                    if (mode == ExportMode.SimHub)
                    {
                        simHubSessionId = CreateRandomUInt64();
                        simHubSessionStartUtc = DateTime.UtcNow;
                    }

                    Thread.Sleep(250);
                }
            }

            Console.WriteLine("Stopped.");
        }

        private static FlightTelemetry ReadTelemetry(ClodReader reader, TelemetryState state)
        {
            double heading840 = SafeReadSlot(reader, 840);
            double pitch841 = SafeReadSlot(reader, 841);
            double roll842 = SafeReadSlot(reader, 842);
            double heading1769 = SafeReadSlot(reader, 1769);

            // New live instrument-style candidates
            double speed1609 = SafeReadSlot(reader, 1609);   // I_VelocityIAS[-1]
            double rpm1481 = SafeReadSlot(reader, 1481);     // I_EngineRPM[1]

            double heading = SelectHeading(heading1769, heading840);

            DateTime now = DateTime.UtcNow;

            double pitchRate = 0.0;
            double rollRate = 0.0;
            double yawRate = 0.0;

            if (state.HasPrevious)
            {
                double dt = (now - state.PrevTimeUtc).TotalSeconds;

                if (dt > 0.0001)
                {
                    pitchRate = (pitch841 - state.PrevPitch) / dt;
                    rollRate = WrappedDeltaDegrees(roll842, state.PrevRoll) / dt;
                    yawRate = WrappedDeltaDegrees(heading, state.PrevHeading) / dt;
                }
            }

            state.SmoothedPitchRate = Smooth(state.SmoothedPitchRate, pitchRate, SmoothingAlpha);
            state.SmoothedRollRate = Smooth(state.SmoothedRollRate, rollRate, SmoothingAlpha);
            state.SmoothedYawRate = Smooth(state.SmoothedYawRate, yawRate, SmoothingAlpha);

            state.PrevPitch = pitch841;
            state.PrevRoll = roll842;
            state.PrevHeading = heading;
            state.PrevTimeUtc = now;
            state.HasPrevious = true;

            return new FlightTelemetry
            {
                IsValid = IsFinite(pitch841) && IsFinite(roll842) && IsFinite(heading),
                Heading840 = heading840,
                Heading1769 = heading1769,
                HeadingSelected = heading,
                PitchDeg = pitch841,
                RollDeg = roll842,
                PitchRateDegPerSec = state.SmoothedPitchRate,
                RollRateDegPerSec = state.SmoothedRollRate,
                YawRateDegPerSec = state.SmoothedYawRate,
                SpeedIAS = IsFinite(speed1609) ? speed1609 : 0.0,
                EngineRpm = IsFinite(rpm1481) ? rpm1481 : 0.0
            };
        }

        private static SrsTelemetryPacket BuildSrsPacket(FlightTelemetry telemetry)
        {
            return new SrsTelemetryPacket
            {
                apiMode = ApiMode,
                version = ApiVersion,
                game = Game,
                vehicleName = Vehicle,
                location = Location,

                // IAS units may vary by aircraft in CloD, but this is still much better than zero.
                speed = (float)telemetry.SpeedIAS,
                rpm = (float)telemetry.EngineRpm,
                maxRpm = 4000f,
                gear = 0,

                pitch = (float)telemetry.PitchDeg,
                roll = (float)telemetry.RollDeg,
                yaw = (float)telemetry.HeadingSelected,

                lateralVelocity = 0f,
                lateralAcceleration = 0f,
                verticalAcceleration = 0f,
                longitudinalAcceleration = 0f,

                suspensionTravelFrontLeft = 0f,
                suspensionTravelFrontRight = 0f,
                suspensionTravelRearLeft = 0f,
                suspensionTravelRearRight = 0f,

                wheelTerrainFrontLeft = 0,
                wheelTerrainFrontRight = 0,
                wheelTerrainRearLeft = 0,
                wheelTerrainRearRight = 0
            };
        }

        private static SimHubTelemetryPacket BuildSimHubPacket(
            FlightTelemetry telemetry,
            ulong emitterInstanceId,
            ulong sessionId,
            ref ulong packetCounter,
            DateTime sessionStartUtc)
        {
            SimHubTelemetryPacket packet = SimHubTelemetryPacket.CreateDefault();

            packet.EmitterInstanceId = emitterInstanceId;
            packet.PacketId = 0;
            packet.PacketsCounter = ++packetCounter;
            packet.IsSessionRunning = 1;
            packet.IsSessionPaused = 0;
            packet.SessionId = sessionId;
            packet.IsReplay = 0;
            packet.IsUserInControl = 1;
            packet.IsAIInControl = 0;
            packet.IsSpectator = 0;
            packet.SessionTimeSeconds = (DateTime.UtcNow - sessionStartUtc).TotalSeconds;
            packet.PhysicsDiscontinuityCounter = 0;

            packet.YawDegrees = (float)telemetry.HeadingSelected;
            packet.PitchDegrees = (float)telemetry.PitchDeg;
            packet.RollDegrees = (float)telemetry.RollDeg;

            return packet;
        }

        private static bool TryParseArgs(string[] args, out ExportMode mode)
        {
            mode = default;

            if (args.Length != 1)
                return false;

            string arg = args[0].Trim();

            if (arg.Equals("-srs", StringComparison.OrdinalIgnoreCase))
            {
                mode = ExportMode.Srs;
                return true;
            }

            if (arg.Equals("-simhub", StringComparison.OrdinalIgnoreCase))
            {
                mode = ExportMode.SimHub;
                return true;
            }

            return false;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  ClodTelemetryBridge.exe -srs");
            Console.WriteLine("  ClodTelemetryBridge.exe -simhub");
        }

        private static double SafeReadSlot(ClodReader reader, int slot)
        {
            double value = reader.ReadSlot(slot);
            return IsFinite(value) ? value : 0.0;
        }

        private static double SelectHeading(double heading1769, double heading840)
        {
            if (IsFinite(heading1769) && heading1769 >= -0.5 && heading1769 <= 360.5)
                return Normalize360(heading1769);

            if (IsFinite(heading840))
                return Normalize360(heading840);

            return 0.0;
        }

        private static double WrappedDeltaDegrees(double current, double previous)
        {
            double delta = current - previous;

            while (delta > 180.0)
                delta -= 360.0;

            while (delta < -180.0)
                delta += 360.0;

            return delta;
        }

        private static double Normalize360(double value)
        {
            value %= 360.0;

            if (value < 0.0)
                value += 360.0;

            return value;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double Smooth(double currentSmoothed, double newValue, double alpha)
        {
            return (currentSmoothed * alpha) + (newValue * (1.0 - alpha));
        }

        private static char[] FixedChars(string text, int length)
        {
            return text.PadRight(length).ToCharArray();
        }

        private static byte[] StructToBytes<T>(T packet) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            byte[] bytes = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);

            try
            {
                Marshal.StructureToPtr(packet, ptr, false);
                Marshal.Copy(ptr, bytes, 0, size);
                return bytes;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        private static ulong CreateRandomUInt64()
        {
            byte[] b = Guid.NewGuid().ToByteArray();
            return BitConverter.ToUInt64(b, 0);
        }
    }
}