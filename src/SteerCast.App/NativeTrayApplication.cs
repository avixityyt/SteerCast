using System.Runtime.InteropServices;
using System.Windows.Forms;
using SteerCast.App.Services;

namespace SteerCast.App;

public sealed class NativeTrayApplication : IDisposable
{
    private const uint WmApp = 0x8000;
    private const uint WmTray = WmApp + 1;
    private const uint WmDestroy = 0x0002;
    private const uint WmLButtonDoubleClick = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint NifMessage = 0x0001;
    private const uint NifIcon = 0x0002;
    private const uint NifTip = 0x0004;
    private const uint NimAdd = 0x0000;
    private const uint NimDelete = 0x0002;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x0010;
    private const uint LrDefaultSize = 0x0040;
    private const uint MfString = 0x0000;
    private const uint MfSeparator = 0x0800;
    private const uint MfChecked = 0x0008;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    private const uint MenuOpen = 1;
    private const uint MenuCopy = 2;
    private const uint MenuRefresh = 3;
    private const uint MenuStartup = 4;
    private const uint MenuAlwaysOnTop = 5;
    private const uint MenuExit = 6;

    private readonly LocalServer _server;
    private readonly IWheelInputSource _inputSource;
    private readonly string _setupUrl;
    private readonly string _overlayUrl;
    private readonly string _iconPath;
    private bool _alwaysOnTop;
    private readonly WindowProcedure _windowProcedure;
    private readonly string _windowClass = $"SteerCastTray_{Environment.ProcessId}";
    private IntPtr _window;
    private IntPtr _customIcon;
    private NotifyIconData _iconData;
    private SetupWindow? _setupWindow;
    private bool _disposed;

    public NativeTrayApplication(LocalServer server, IWheelInputSource inputSource, string baseUrl, string iconPath, bool alwaysOnTop = false)
    {
        _server = server;
        _inputSource = inputSource;
        _setupUrl = $"{baseUrl}setup";
        _overlayUrl = $"{baseUrl}overlay/default";
        _iconPath = iconPath;
        _alwaysOnTop = alwaysOnTop;
        _windowProcedure = WindowProc;
        CreateMessageWindow();
        AddTrayIcon();
    }

    public void Run()
    {
        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
    }

    private void CreateMessageWindow()
    {
        var module = GetModuleHandle(null);
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Instance = module,
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(_windowProcedure),
            ClassName = _windowClass
        };

        if (RegisterClassEx(ref windowClass) == 0)
        {
            throw new InvalidOperationException("Could not register the SteerCast tray window.");
        }

        _window = CreateWindowEx(
            0,
            _windowClass,
            "SteerCast",
            0,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            module,
            IntPtr.Zero);

        if (_window == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not create the SteerCast tray window.");
        }
    }

    private void AddTrayIcon()
    {
        _iconData = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            Window = _window,
            Id = 1,
            Flags = NifMessage | NifIcon | NifTip,
            CallbackMessage = WmTray,
            Icon = LoadTrayIcon(),
            Tip = "SteerCast"
        };

        if (!ShellNotifyIcon(NimAdd, ref _iconData))
        {
            throw new InvalidOperationException("Could not add the SteerCast notification icon.");
        }
    }

    private IntPtr LoadTrayIcon()
    {
        if (File.Exists(_iconPath))
        {
            _customIcon = LoadImage(IntPtr.Zero, _iconPath, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
            if (_customIcon != IntPtr.Zero)
            {
                return _customIcon;
            }
        }

        return LoadIcon(IntPtr.Zero, new IntPtr(32512));
    }

    private IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmTray)
        {
            switch ((uint)lParam.ToInt64())
            {
                case WmLButtonDoubleClick:
                    OpenSetup();
                    return IntPtr.Zero;
                case WmRButtonUp:
                    ShowMenu();
                    return IntPtr.Zero;
            }
        }
        else if (message == WmDestroy)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return DefWindowProc(window, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendMenu(menu, MfString, MenuOpen, "Open setup");
            AppendMenu(menu, MfString, MenuCopy, "Copy OBS URL");
            AppendMenu(menu, MfString, MenuRefresh, "Reconnect devices");
            AppendMenu(menu, MfString | (StartupRegistration.IsEnabled() ? MfChecked : 0), MenuStartup, "Launch at sign-in");
            AppendMenu(menu, MfString | (_alwaysOnTop ? MfChecked : 0), MenuAlwaysOnTop, "Keep setup on top");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, MenuExit, "Exit");

            GetCursorPos(out var point);
            SetForegroundWindow(_window);
            var command = TrackPopupMenu(menu, TpmRightButton | TpmReturnCommand, point.X, point.Y, 0, _window, IntPtr.Zero);
            ExecuteMenuCommand(command);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void ExecuteMenuCommand(uint command)
    {
        switch (command)
        {
            case MenuOpen:
                OpenSetup();
                break;
            case MenuCopy:
                SetClipboardText(_overlayUrl);
                break;
            case MenuRefresh:
                _inputSource.Refresh();
                break;
            case MenuStartup:
                StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled());
                break;
            case MenuAlwaysOnTop:
                _alwaysOnTop = !_alwaysOnTop;
                if (_setupWindow is { IsDisposed: false } setupWindow)
                {
                    setupWindow.TopMost = _alwaysOnTop;
                    if (_alwaysOnTop)
                    {
                        setupWindow.Activate();
                    }
                }
                break;
            case MenuExit:
                Dispose();
                break;
        }
    }

    public void OpenSetup(bool showLaunchSplash = false)
    {
        if (_disposed)
        {
            return;
        }

        var setupUrl = showLaunchSplash ? $"{_setupUrl}?launch=1" : _setupUrl;
        if (_setupWindow is { IsDisposed: false } existing)
        {
            existing.Navigate(setupUrl);
            if (existing.WindowState == FormWindowState.Minimized)
            {
                existing.WindowState = FormWindowState.Normal;
            }

            existing.Show();
            existing.Activate();
            return;
        }

        _setupWindow = new SetupWindow(setupUrl, _iconPath) { TopMost = _alwaysOnTop };
        _setupWindow.FormClosed += (_, _) => _setupWindow = null;
        _setupWindow.Show();
        _setupWindow.Activate();
    }

    private static void SetClipboardText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            return;
        }

        try
        {
            EmptyClipboard();
            var bytes = (text.Length + 1) * sizeof(char);
            var memory = GlobalAlloc(GmemMoveable, (nuint)bytes);
            if (memory == IntPtr.Zero)
            {
                return;
            }

            var destination = GlobalLock(memory);
            try
            {
                Marshal.Copy(text.ToCharArray(), 0, destination, text.Length);
                Marshal.WriteInt16(destination, text.Length * sizeof(char), 0);
            }
            finally
            {
                GlobalUnlock(memory);
            }

            if (SetClipboardData(CfUnicodeText, memory) == IntPtr.Zero)
            {
                GlobalFree(memory);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var setupWindow = _setupWindow;
        _setupWindow = null;
        if (setupWindow is { IsDisposed: false })
        {
            setupWindow.Close();
            setupWindow.Dispose();
        }

        ShellNotifyIcon(NimDelete, ref _iconData);
        if (_customIcon != IntPtr.Zero)
        {
            DestroyIcon(_customIcon);
            _customIcon = IntPtr.Zero;
        }
        _server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (_window != IntPtr.Zero)
        {
            DestroyWindow(_window);
            _window = IntPtr.Zero;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public IntPtr WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;
        public uint InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out NativeMessage message, IntPtr window, uint minimum, uint maximum);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint load);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, uint item, string? text);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr window, IntPtr rectangle);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);
}
