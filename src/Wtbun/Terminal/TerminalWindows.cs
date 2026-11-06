using System.Text;
using System.Windows.Automation;
using Wtbun.Native;

namespace Wtbun.Terminal;

/// <summary>
/// Windows Terminal のトップレベルウィンドウを探す。
/// </summary>
public static class TerminalWindows
{
    public const string WindowClass = "CASCADIA_HOSTING_WINDOW_CLASS";

    /// <param name="visibleOnly">false なら、まだ表示されていないウィンドウも含める。</param>
    public static HashSet<IntPtr> Enumerate(bool visibleOnly)
    {
        var result = new HashSet<IntPtr>();
        var className = new StringBuilder(256);
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            className.Clear();
            NativeMethods.GetClassName(hWnd, className, className.Capacity);
            if (className.ToString() == WindowClass && (!visibleOnly || NativeMethods.IsWindowVisible(hWnd)))
            {
                result.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static bool Exists(IntPtr hWnd) => NativeMethods.IsWindow(hWnd);

    /// <summary>表示されている Windows Terminal のウィンドウか。</summary>
    public static bool IsTerminalWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd) || !NativeMethods.IsWindowVisible(hWnd))
        {
            return false;
        }
        var className = new StringBuilder(256);
        NativeMethods.GetClassName(hWnd, className, className.Capacity);
        return className.ToString() == WindowClass;
    }

    /// <summary>
    /// 最小化されていれば元に戻し、最前面に出す。
    /// 前面に出せるのは wtbun が前面にいるとき（利用者が wtbun を操作した直後）だけ。
    /// </summary>
    public static void Activate(IntPtr hWnd)
    {
        if (NativeMethods.IsIconic(hWnd))
        {
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
        }
        NativeMethods.SetForegroundWindow(hWnd);
    }

    /// <summary>
    /// 閉じるよう求める。タブが複数あると Windows Terminal が確認を出すので、すぐには閉じないことがある。
    /// </summary>
    public static void RequestClose(IntPtr hWnd)
    {
        if (!TerminalCloseGuard.TryCloseNow(hWnd))
        {
            NativeMethods.PostMessage(hWnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
    }

    /// <summary>
    /// 「すべてのタブを閉じますか？」の確認が出ているか。確認の「すべて閉じる」ボタン（AutomationId が PrimaryButton。
    /// 表示の言語によらない）があるかを UI オートメーションで調べる。調べられなければ false。
    /// </summary>
    public static bool IsConfirmingClose(IntPtr hWnd)
    {
        try
        {
            var window = AutomationElement.FromHandle(hWnd);
            return window.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.AutomationIdProperty, "PrimaryButton"))) is not null;
        }
        catch (Exception e) when (e is ElementNotAvailableException or InvalidOperationException or ArgumentException
            or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }
}
