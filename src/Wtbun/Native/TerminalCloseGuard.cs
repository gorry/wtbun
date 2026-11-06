using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Wtbun.Native;

/// <summary>
/// wtbunhook.dll を Windows Terminal に読み込ませ、ウィンドウに届く「閉じる」要求（タスクバーやシステムメニューから）を止めて、
/// <see cref="CloseRequestedMessage"/> で知らせてもらう。DLL がない・読み込めないときは何もしない（確認なしで閉じる通常の動き）。
/// </summary>
/// <remarks>UI スレッドから使う。フックを付けたスレッドが終わるとフックも外れる。</remarks>
public sealed class TerminalCloseGuard(IntPtr owner) : IDisposable
{
    private const string DllName = "wtbunhook.dll";
    private const uint TimeoutMs = 1000;
    private static readonly IntPtr Answered = new(0x57544255);

    private static readonly uint AttachMessage = NativeMethods.RegisterWindowMessage("wtbun.hook.attach");
    private static readonly uint DetachMessage = NativeMethods.RegisterWindowMessage("wtbun.hook.detach");
    private static readonly uint CloseNowMessage = NativeMethods.RegisterWindowMessage("wtbun.hook.closeNow");

    /// <summary>閉じる要求を止めたときに <c>owner</c> へ届く。wParam がウィンドウ。</summary>
    public static readonly int CloseRequestedMessage = (int)NativeMethods.RegisterWindowMessage("wtbun.hook.closeRequested");

    private readonly Dictionary<uint, IntPtr> _hooksByThread = [];
    private readonly HashSet<IntPtr> _windows = [];
    private IntPtr _module;
    private IntPtr _hookProc;
    private bool _unavailable;
    private bool _disposed;

    public void Attach(IntPtr hWnd)
    {
        if (_disposed || !EnsureLoaded())
        {
            return;
        }
        var thread = NativeMethods.GetWindowThreadProcessId(hWnd, out _);
        if (thread == 0)
        {
            return;
        }
        if (!_hooksByThread.ContainsKey(thread))
        {
            var hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_CALLWNDPROC, _hookProc, _module, thread);
            if (hook == IntPtr.Zero)
            {
                Debug.WriteLine($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
                return;
            }
            _hooksByThread[thread] = hook;
        }
        if (Send(hWnd, AttachMessage, owner) == Answered)
        {
            _windows.Add(hWnd);
        }
        else
        {
            Debug.WriteLine($"Terminal window {hWnd} did not accept the close guard");
        }
    }

    /// <summary>
    /// 閉じる要求を止めずに閉じる。登録していないウィンドウなら false を返すので、呼び出し側が普通に閉じるよう求める。
    /// </summary>
    public static bool TryCloseNow(IntPtr hWnd) => Send(hWnd, CloseNowMessage, IntPtr.Zero) == Answered;

    private bool EnsureLoaded()
    {
        if (_hookProc != IntPtr.Zero)
        {
            return true;
        }
        if (_unavailable)
        {
            return false;
        }
        try
        {
            var path = PrepareDll();
            _module = NativeMethods.LoadLibrary(path);
            if (_module != IntPtr.Zero)
            {
                _hookProc = NativeMethods.GetProcAddress(_module, "HookProc");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(ex);
        }
        _unavailable = _hookProc == IntPtr.Zero;
        return !_unavailable;
    }

    /// <summary>
    /// 実行フォルダーの DLL を、ユーザーのフォルダーにコピーして使う。wtbun が強制終了すると Windows Terminal が終わるまで
    /// DLL がロックされるので、そのままではビルドや更新で上書きできなくなる。中身が同じなら作り直さない。
    /// </summary>
    private static string PrepareDll()
    {
        var source = Path.Combine(AppContext.BaseDirectory, DllName);
        var bytes = File.ReadAllBytes(source);
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wtbun", "hook");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, $"wtbunhook-{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16]}.dll");
        if (!File.Exists(target) || !File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes))
        {
            File.WriteAllBytes(target, bytes);
        }
        return target;
    }

    private static IntPtr Send(IntPtr hWnd, uint message, IntPtr wParam) =>
        NativeMethods.SendMessageTimeout(hWnd, message, wParam, IntPtr.Zero, NativeMethods.SMTO_ABORTIFHUNG, TimeoutMs, out var result) != IntPtr.Zero
            ? result
            : IntPtr.Zero;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        foreach (var hWnd in _windows.Where(NativeMethods.IsWindow))
        {
            Send(hWnd, DetachMessage, IntPtr.Zero);
        }
        foreach (var hook in _hooksByThread.Values)
        {
            NativeMethods.UnhookWindowsHookEx(hook);
        }
        if (_module != IntPtr.Zero)
        {
            NativeMethods.FreeLibrary(_module);
        }
    }
}
