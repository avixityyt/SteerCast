using System.Runtime.InteropServices;
using SteerCast.Core.Models;

namespace SteerCast.App.Services;

/// <summary>
/// Reads optional Logitech SDK state from a user-installed local SDK copy.
/// This adapter never creates, modifies, starts, or stops force-feedback effects.
/// </summary>
public sealed class LogitechSdkForceFeedbackAdapter : IForceFeedbackAdapter
{
    private const string WrapperFileName = "LogitechSteeringWheelEnginesWrapper.dll";
    private readonly IntPtr _library;
    private readonly LogiInitialize _initialize;
    private readonly LogiUpdate _update;
    private readonly LogiIsConnected _isConnected;
    private readonly LogiGetState _getState;
    private readonly LogiShutdown _shutdown;
    private ForceFeedbackReading _status;

    private LogitechSdkForceFeedbackAdapter(
        IntPtr library,
        LogiInitialize initialize,
        LogiUpdate update,
        LogiIsConnected isConnected,
        LogiGetState getState,
        LogiShutdown shutdown,
        ForceFeedbackReading status)
    {
        _library = library;
        _initialize = initialize;
        _update = update;
        _isConnected = isConnected;
        _getState = getState;
        _shutdown = shutdown;
        _status = status;
    }

    public ForceFeedbackReading Status => _status;

    public static IForceFeedbackAdapter CreateOrFallback()
    {
        var gHub = LogitechGHubDetector.Detect();
        var wrapperPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteerCast", "adapters", "logitech-wheel-sdk", "Lib", "GameEnginesWrapper", "x64", WrapperFileName);

        if (!File.Exists(wrapperPath))
        {
            return new NullForceFeedbackAdapter();
        }

        try
        {
            if (!NativeLibrary.TryLoad(wrapperPath, out var library))
            {
                return new UnavailableForceFeedbackAdapter(
                    "logitech-sdk",
                    "The Logitech SDK wrapper could not load. Its Logitech runtime dependency may be missing.",
                    gHub);
            }

            var adapter = new LogitechSdkForceFeedbackAdapter(
                library,
                LoadDelegate<LogiInitialize>(library, "LogiSteeringInitialize"),
                LoadDelegate<LogiUpdate>(library, "LogiUpdate"),
                LoadDelegate<LogiIsConnected>(library, "LogiIsConnected"),
                LoadDelegate<LogiGetState>(library, "LogiGetStateENGINES"),
                LoadDelegate<LogiShutdown>(library, "LogiSteeringShutdown"),
                new ForceFeedbackReading(null, null, "logitech-sdk", false, "Logitech SDK loaded. Checking for a compatible wheel…", gHub.Installed, gHub.Running));

            if (!adapter._initialize(true))
            {
                adapter.Dispose();
                return new UnavailableForceFeedbackAdapter(
                    "logitech-sdk",
                    "The Logitech SDK wrapper loaded, but could not initialize the wheel service.",
                    gHub);
            }

            return adapter;
        }
        catch (Exception exception) when (exception is BadImageFormatException or EntryPointNotFoundException or DllNotFoundException)
        {
            return new UnavailableForceFeedbackAdapter("logitech-sdk", $"Logitech SDK could not start: {exception.Message}", gHub);
        }
    }

    public ForceFeedbackReading? Read(string deviceId)
    {
        try
        {
            if (!_update() || !_isConnected(0))
            {
                _status = _status with { Available = false, Status = "Logitech SDK loaded. No compatible wheel is currently available to the SDK." };
                return null;
            }

            var pointer = _getState(0);
            if (pointer == IntPtr.Zero)
            {
                _status = _status with { Available = false, Status = "Logitech SDK did not return a wheel state." };
                return null;
            }

            var state = Marshal.PtrToStructure<LogiState>(pointer);
            var force = SelectLargest(("Fx", state.Fx), ("Fy", state.Fy), ("Fz", state.Fz));
            var torque = SelectLargest(("FRx", state.FRx), ("FRy", state.FRy), ("FRz", state.FRz));
            _status = _status with
            {
                Force = force.Value,
                Torque = torque.Value,
                Available = true,
                Status = "Logitech SDK is reporting wheel force and torque.",
                ForceAxis = force.Axis,
                TorqueAxis = torque.Axis
            };
            return new ForceFeedbackReading(state.Fx, state.FRx, "logitech-sdk", true, _status.Status, _status.GHubInstalled, _status.GHubRunning);
        }
        catch (Exception exception) when (exception is AccessViolationException or SEHException)
        {
            _status = _status with { Available = false, Status = "Logitech SDK returned an invalid wheel state." };
            return null;
        }
    }

    public void Dispose()
    {
        try
        {
            _shutdown();
        }
        finally
        {
            if (_library != IntPtr.Zero)
            {
                NativeLibrary.Free(_library);
            }
        }
    }

    private static T LoadDelegate<T>(IntPtr library, string export) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, export));

    private static (string Axis, int Value) SelectLargest(params (string Axis, int Value)[] axes) =>
        axes.MaxBy(axis => Math.Abs((long)axis.Value));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool LogiInitialize([MarshalAs(UnmanagedType.I1)] bool ignoreXInputControllers);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool LogiUpdate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool LogiIsConnected(int index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr LogiGetState(int index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void LogiShutdown();

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct LogiState
    {
        public int X;
        public int Y;
        public int Z;
        public int Rx;
        public int Ry;
        public int Rz;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public int[] Slider;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public uint[] Pov;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)] public byte[] Buttons;
        public int Vx;
        public int Vy;
        public int Vz;
        public int VRx;
        public int VRy;
        public int VRz;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public int[] VSlider;
        public int Ax;
        public int Ay;
        public int Az;
        public int ARx;
        public int ARy;
        public int ARz;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public int[] ASlider;
        public int Fx;
        public int Fy;
        public int Fz;
        public int FRx;
        public int FRy;
        public int FRz;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public int[] FSlider;
    }

    private sealed class UnavailableForceFeedbackAdapter(string source, string status, GHubStatus gHub) : IForceFeedbackAdapter
    {
        public ForceFeedbackReading Status { get; } = new(null, null, source, false, status, gHub.Installed, gHub.Running);
        public ForceFeedbackReading? Read(string deviceId) => null;
        public void Dispose() { }
    }
}
