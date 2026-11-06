using System.Runtime.InteropServices;

namespace Wtbun.Native;

/// <summary>
/// ほかのプロセスのウィンドウも含めて、タスクバーのボタンを出し入れする（ITaskbarList）。UI スレッドから呼ぶ。
/// エクスプローラーが再起動するとボタンは元に戻るので、そのときは出し入れし直す。
/// </summary>
public static class Taskbar
{
    [ComImport]
    [Guid("56FDF342-FD6D-11d0-958A-006097C9A090")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    private class TaskbarList;

    private static ITaskbarList? _instance;

    private static ITaskbarList Instance
    {
        get
        {
            if (_instance is null)
            {
                _instance = (ITaskbarList)new TaskbarList();
                _instance.HrInit();
            }
            return _instance;
        }
    }

    public static void SetButtonVisible(IntPtr hWnd, bool visible)
    {
        try
        {
            if (visible)
            {
                Instance.AddTab(hWnd);
            }
            else
            {
                Instance.DeleteTab(hWnd);
            }
        }
        catch (COMException)
        {
            // エクスプローラーが動いていないときなど。次に出し入れし直すときに任せる
            _instance = null;
        }
    }

    /// <summary>エクスプローラーが再起動してタスクバーが作り直されたときに届くメッセージ。</summary>
    public static readonly int TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string lpString);
}
