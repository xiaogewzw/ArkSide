using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GameSidebar.TestWindow;

[SupportedOSPlatform("windows")]
internal static class Program
{
    private const int WsOverlappedWindow = 0x00CF0000;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int SwShow = 5;
    private const int SwpFrameChanged = 0x0020;
    private const int SwpNoMove = 0x0002;
    private const int SwpNoZOrder = 0x0004;
    private static readonly WndProc Callback = WindowProc;
    private static nint _window;
    private static bool _borderless;
    private static bool _alternateTitle;
    private static bool _recreating;
    private static int _clientWidth = 1920;
    private static int _clientHeight = 1080;
    private static string _baseTitle = "GameSidebar TestWindow";

    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("TestWindow 仅支持 Windows"); return 1; }
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--width" && i + 1 < args.Length && int.TryParse(args[++i], out var w)) _clientWidth = w;
            else if (args[i] == "--height" && i + 1 < args.Length && int.TryParse(args[++i], out var h)) _clientHeight = h;
            else if (args[i] == "--title" && i + 1 < args.Length) _baseTitle = args[++i];
            else if (args[i] == "--borderless") _borderless = true;
        }
        if (_clientWidth <= 0 || _clientHeight <= 0) { Console.Error.WriteLine("Client 尺寸必须为正数"); return 2; }
        if (!SetProcessDpiAwarenessContext(new nint(-4)))
        {
            Console.Error.WriteLine($"无法设置 PerMonitorV2 DPI awareness：{Marshal.GetLastWin32Error()}");
            return 5;
        }
        var instance = GetModuleHandle(null);
        var windowClass = new WndClass
        {
            Style = 0, WindowProc = Marshal.GetFunctionPointerForDelegate(Callback), Instance = instance,
            ClassName = "GameSidebarControlledWindow", Cursor = LoadCursor(0, new nint(32512))
        };
        if (RegisterClass(ref windowClass) == 0) { Console.Error.WriteLine($"RegisterClass: {Marshal.GetLastWin32Error()}"); return 3; }
        _window = CreateWindow(instance);
        if (_window == 0) { Console.Error.WriteLine($"CreateWindowEx: {Marshal.GetLastWin32Error()}"); return 4; }
        ShowWindow(_window, SwShow);
        Resize();
        Console.WriteLine("B：边框切换；S：1920×1080/1280×720；T：标题切换；R：同进程重建窗口；Esc：关闭。可启动第二实例验证多候选。");
        while (GetMessage(out var message, 0, 0, 0) > 0) { TranslateMessage(ref message); DispatchMessage(ref message); }
        return 0;
    }
    private static nint CreateWindow(nint instance)
    {
        var style = _borderless ? WsPopup : WsOverlappedWindow;
        var bounds = new Rect { Left = 0, Top = 0, Right = _clientWidth, Bottom = _clientHeight };
        AdjustWindowRectExForDpi(ref bounds, style, false, 0, GetDpiForSystem());
        return CreateWindowEx(0, "GameSidebarControlledWindow", _baseTitle, style, 100, 100,
            bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, 0, 0, instance, 0);
    }
    private static void Resize()
    {
        var style = _borderless ? WsPopup : WsOverlappedWindow;
        SetWindowLongPtr(_window, -16, new nint(style));
        var dpi = GetDpiForWindow(_window);
        var rect = new Rect { Right = _clientWidth, Bottom = _clientHeight };
        AdjustWindowRectExForDpi(ref rect, style, false, 0, dpi);
        SetWindowPos(_window, 0, 0, 0, rect.Right - rect.Left, rect.Bottom - rect.Top,
            SwpFrameChanged | SwpNoMove | SwpNoZOrder);
        UpdateTitle();
    }
    private static void UpdateTitle()
    {
        if (_window == 0 || !GetClientRect(_window, out var client)) return;
        var origin = new Point(); ClientToScreen(_window, ref origin);
        var suffix = _alternateTitle ? " Alternate" : "";
        SetWindowText(_window, $"{_baseTitle}{suffix} | Client {client.Right - client.Left}x{client.Bottom - client.Top} px | ({origin.X},{origin.Y}) | PID {Environment.ProcessId}");
    }
    private static nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case 0x0005: // WM_SIZE
            case 0x0003: // WM_MOVE
                if (_window == hwnd) UpdateTitle();
                break;
            case 0x0100: // WM_KEYDOWN
                switch ((int)wParam)
                {
                    case 0x42: _borderless = !_borderless; Resize(); return 0;
                    case 0x53: (_clientWidth, _clientHeight) = _clientWidth == 1920 ? (1280, 720) : (1920, 1080); Resize(); return 0;
                    case 0x54: _alternateTitle = !_alternateTitle; UpdateTitle(); return 0;
                    case 0x52:
                        _recreating = true; DestroyWindow(_window); _window = CreateWindow(GetModuleHandle(null));
                        _recreating = false; ShowWindow(_window, SwShow); Resize(); return 0;
                    case 0x1B: DestroyWindow(hwnd); return 0;
                }
                break;
            case 0x0002: // WM_DESTROY
                if (!_recreating) PostQuitMessage(0);
                return 0;
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }
    private delegate nint WndProc(nint hwnd, uint message, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WndClass
    {
        public uint Style; public nint WindowProc; public int ClassExtra; public int WindowExtra;
        public nint Instance; public nint Icon; public nint Cursor; public nint Background;
        [MarshalAs(UnmanagedType.LPWStr)] public string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Message
    {
        public nint Hwnd; public uint Id; public nint WParam; public nint LParam;
        public uint Time; public Point Point; public uint Private;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClass(ref WndClass value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateWindowExW")]
    private static extern nint CreateWindowEx(uint exStyle, string className, string title, int style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetMessage(out Message message, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref Message message);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern nint LoadCursor(nint instance, nint cursor);
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] private static extern bool AdjustWindowRectExForDpi(ref Rect rect, int style, bool menu, uint exStyle, uint dpi);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(nint hwnd, string title);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref Point point);
}
