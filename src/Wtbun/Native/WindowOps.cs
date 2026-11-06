using System.Runtime.InteropServices;

namespace Wtbun.Native;

/// <summary>
/// ほかのプロセスのウィンドウも含めた、位置・サイズ・状態の操作。座標は物理ピクセル。
/// </summary>
public static class WindowOps
{
    public static RECT GetRect(IntPtr hWnd) => NativeMethods.GetWindowRect(hWnd, out var rect) ? rect : default;

    /// <summary>
    /// 見えている枠の矩形。Windows 10 以降の GetWindowRect は、見えないリサイズ用の縁を含む。
    /// </summary>
    public static RECT GetFrameBounds(IntPtr hWnd) =>
        NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out var rect, Marshal.SizeOf<RECT>()) == 0
            ? rect
            : GetRect(hWnd);

    /// <summary>前後関係と前面かどうかは変えずに、位置とサイズだけを変える。</summary>
    public static void Move(IntPtr hWnd, RECT rect) =>
        NativeMethods.SetWindowPos(hWnd, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);

    /// <summary>前面かどうかは変えずに、前後関係でいちばん上（最前面指定のウィンドウを除く）に置く。</summary>
    public static void BringToTopWithoutActivating(IntPtr hWnd) =>
        NativeMethods.SetWindowPos(hWnd, NativeMethods.HWND_TOP, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

    /// <summary>
    /// 前面かどうかは変えずに、前後関係でいちばん上（最前面指定のウィンドウを除く）に持ち上げる。
    /// いったん最前面指定にしてからすぐに外す。Windows Terminal のウィンドウは、HWND_TOP や「〜のすぐ後ろ」の指示では
    /// 思った位置に来ない（「すぐ後ろ」を指示すると上に来る）が、この方法なら持ち上がる。
    /// </summary>
    public static void LiftWithoutActivating(IntPtr hWnd)
    {
        const uint flags = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE;
        NativeMethods.SetWindowPos(hWnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, flags);
        NativeMethods.SetWindowPos(hWnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, flags);
    }

    public static bool IsForeground(IntPtr hWnd) => hWnd != IntPtr.Zero && NativeMethods.GetForegroundWindow() == hWnd;

    /// <summary>
    /// ほかのプロセスが前面にいるときに、自分のウィンドウを前面に出す。利用者が自分のウィンドウを操作していないと
    /// SetForegroundWindow は断られるので、その間だけ前面のスレッドと入力の状態を共有する。自分が前面なら何もしない。
    /// </summary>
    /// <returns>それまで前面にいたほかのプロセスのウィンドウ。何もしなかったときは <see cref="IntPtr.Zero"/>。</returns>
    public static IntPtr TakeForeground(IntPtr hWnd)
    {
        var ownThread = NativeMethods.GetCurrentThreadId();
        var previous = NativeMethods.GetForegroundWindow();
        var foregroundThread = NativeMethods.GetWindowThreadProcessId(previous, out _);
        if (foregroundThread == 0 || foregroundThread == ownThread)
        {
            return IntPtr.Zero;
        }
        NativeMethods.AttachThreadInput(ownThread, foregroundThread, true);
        NativeMethods.SetForegroundWindow(hWnd);
        NativeMethods.AttachThreadInput(ownThread, foregroundThread, false);
        return previous;
    }

    public static bool IsMaximized(IntPtr hWnd) => NativeMethods.IsZoomed(hWnd);

    public static bool IsMinimized(IntPtr hWnd) => NativeMethods.IsIconic(hWnd);

    /// <summary>最大化・最小化を元に戻す。前面にいないウィンドウは前面に出さない。</summary>
    public static void Restore(IntPtr hWnd, bool activate) =>
        NativeMethods.ShowWindow(hWnd, activate ? NativeMethods.SW_RESTORE : NativeMethods.SW_SHOWNOACTIVATE);

    /// <summary>
    /// 最小化するよう求める。前面のウィンドウなら、次のウィンドウが前面になる。ShowWindow でほかのプロセスのウィンドウを
    /// 最小化すると、最小化されたウィンドウが前面に残ることがあるので、最小化ボタンと同じくウィンドウ自身に処理させる。
    /// </summary>
    public static void Minimize(IntPtr hWnd) =>
        NativeMethods.PostMessage(hWnd, NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_MINIMIZE, IntPtr.Zero);

    /// <summary>ウィンドウがいるモニターの作業領域（タスクバーを除いた範囲）。</summary>
    public static RECT GetWorkArea(IntPtr hWnd)
    {
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        var monitor = NativeMethods.MonitorFromWindow(hWnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return NativeMethods.GetMonitorInfo(monitor, ref info) ? info.rcWork : GetRect(hWnd);
    }

    /// <summary>矩形が、今つながっているどれかのモニターに重なっているか。</summary>
    public static bool IsOnScreen(RECT rect) =>
        NativeMethods.MonitorFromRect(rect, NativeMethods.MONITOR_DEFAULTTONULL) != IntPtr.Zero;

    public static int GetDpi(IntPtr hWnd) => NativeMethods.GetDpiForWindow(hWnd) is var dpi and > 0 ? (int)dpi : 96;
}
