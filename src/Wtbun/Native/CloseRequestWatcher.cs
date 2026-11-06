using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Wtbun.Native;

/// <summary>
/// 登録したウィンドウのタイトルバーの閉じるボタンのクリックと Alt+F4 を、ウィンドウに届く前に受け取って知らせる
/// （WH_MOUSE_LL・WH_KEYBOARD_LL）。閉じるボタンの上かどうかはウィンドウ自身に WM_NCHITTEST で尋ねる。
/// 答えがなければクリックをそのまま通す。
/// </summary>
/// <remarks>
/// マウスとキーボード全体の入力がこのフックを通るので、UI スレッドが忙しくても操作を遅らせないよう、専用のスレッドで受ける。
/// <see cref="CloseRequested"/> もそのスレッドで呼ばれる。
/// </remarks>
public sealed class CloseRequestWatcher : IDisposable
{
    private const uint HitTestTimeoutMs = 100;

    // ネイティブ側から呼ばれる間、ガベージコレクションで消えないよう持っておく
    private readonly NativeMethods.LowLevelHookProc _mouseProc;
    private readonly NativeMethods.LowLevelHookProc _keyboardProc;
    private readonly HashSet<IntPtr> _windows = [];
    private IntPtr _pressed;
    private bool _f4Held;
    private uint _threadId;
    private bool _disposed;

    /// <summary>閉じる操作を受け取ったときに、そのウィンドウを知らせる。専用のスレッドで呼ばれる。</summary>
    public event Action<IntPtr>? CloseRequested;

    public CloseRequestWatcher()
    {
        _mouseProc = OnMouse;
        _keyboardProc = OnKeyboard;
        using var started = new ManualResetEventSlim();
        var thread = new Thread(() => Run(started)) { IsBackground = true, Name = nameof(CloseRequestWatcher) };
        thread.Start();
        started.Wait();
    }

    public void Watch(IntPtr hWnd)
    {
        lock (_windows)
        {
            _windows.Add(hWnd);
        }
    }

    private bool IsWatched(IntPtr hWnd)
    {
        lock (_windows)
        {
            return _windows.Contains(hWnd);
        }
    }

    private void Run(ManualResetEventSlim started)
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        var module = NativeMethods.GetModuleHandle(null);
        var mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, module, 0);
        var keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, module, 0);
        started.Set();
        // 付けられなければ、その操作は Windows Terminal の通常の動きのまま
        if (mouseHook == IntPtr.Zero || keyboardHook == IntPtr.Zero)
        {
            Debug.WriteLine($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
        }
        while (NativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.DispatchMessage(in message);
        }
        NativeMethods.UnhookWindowsHookEx(mouseHook);
        NativeMethods.UnhookWindowsHookEx(keyboardHook);
    }

    private IntPtr OnMouse(int code, IntPtr message, IntPtr lParam)
    {
        if (code >= 0)
        {
            var kind = (uint)message;
            if (kind == NativeMethods.WM_LBUTTONDOWN)
            {
                var window = WindowWithCloseButtonAt(Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam).pt);
                if (window != IntPtr.Zero)
                {
                    _pressed = window;
                    return 1;
                }
            }
            else if (kind == NativeMethods.WM_LBUTTONUP && _pressed != IntPtr.Zero)
            {
                // 押したのを止めたので、離したのも止める。ボタンの外で離したら何もしない（通常のボタンと同じ）
                var pressed = _pressed;
                _pressed = IntPtr.Zero;
                if (WindowWithCloseButtonAt(Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam).pt) == pressed)
                {
                    CloseRequested?.Invoke(pressed);
                }
                return 1;
            }
        }
        return NativeMethods.CallNextHookEx(IntPtr.Zero, code, message, lParam);
    }

    private IntPtr OnKeyboard(int code, IntPtr message, IntPtr lParam)
    {
        if (code >= 0)
        {
            var key = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            if (key.vkCode == NativeMethods.VK_F4)
            {
                var kind = (uint)message;
                if (kind is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
                {
                    // 押したのを止めたので、離したのも止める
                    if (_f4Held)
                    {
                        _f4Held = false;
                        return 1;
                    }
                }
                else if (_f4Held)
                {
                    // 押し続けている間の繰り返し
                    return 1;
                }
                else if (kind == NativeMethods.WM_SYSKEYDOWN && (key.flags & NativeMethods.LLKHF_ALTDOWN) != 0)
                {
                    var window = NativeMethods.GetForegroundWindow();
                    if (IsWatched(window))
                    {
                        _f4Held = true;
                        CloseRequested?.Invoke(window);
                        return 1;
                    }
                }
            }
        }
        return NativeMethods.CallNextHookEx(IntPtr.Zero, code, message, lParam);
    }

    private IntPtr WindowWithCloseButtonAt(NativeMethods.POINT point)
    {
        var target = NativeMethods.WindowFromPoint(point);
        var root = NativeMethods.GetAncestor(target, NativeMethods.GA_ROOT);
        if (!IsWatched(root))
        {
            return IntPtr.Zero;
        }
        var position = (IntPtr)(((point.Y & 0xFFFF) << 16) | (point.X & 0xFFFF));
        var answered = NativeMethods.SendMessageTimeout(target, NativeMethods.WM_NCHITTEST, IntPtr.Zero, position,
            NativeMethods.SMTO_ABORTIFHUNG, HitTestTimeoutMs, out var hit);
        return answered != IntPtr.Zero && hit == NativeMethods.HTCLOSE ? root : IntPtr.Zero;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
