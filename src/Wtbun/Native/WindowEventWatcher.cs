namespace Wtbun.Native;

/// <summary>
/// ほかのプロセスのウィンドウの、前面化・移動・リサイズ・最小化を知らせる（SetWinEventHook）。
/// イベントはフックを登録したスレッド（UI スレッド）で届く。
/// </summary>
public sealed class WindowEventWatcher : IDisposable
{
    // ネイティブ側から呼ばれる間、ガベージコレクションで消えないよう持っておく
    private readonly NativeMethods.WinEventProc _proc;
    private readonly Dictionary<uint, IntPtr[]> _hooksByProcess = [];

    public event Action<IntPtr>? Foreground;
    public event Action<IntPtr>? MoveSizeStarted;
    public event Action<IntPtr>? MoveSizeEnded;
    public event Action<IntPtr>? LocationChanged;
    public event Action<IntPtr>? MinimizeStarted;
    public event Action<IntPtr>? MinimizeEnded;

    public WindowEventWatcher()
    {
        _proc = OnEvent;
    }

    /// <summary>
    /// ウィンドウのプロセスのイベントを受け取り始める。Windows Terminal は全ウィンドウが 1 つのプロセスなので、
    /// 普通はプロセス 1 つぶんのフックで済む。
    /// </summary>
    public void Watch(IntPtr hWnd)
    {
        NativeMethods.GetWindowThreadProcessId(hWnd, out var processId);
        if (processId == 0 || _hooksByProcess.ContainsKey(processId))
        {
            return;
        }
        _hooksByProcess[processId] =
        [
            NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_MOVESIZEEND,
                IntPtr.Zero, _proc, processId, 0, NativeMethods.WINEVENT_OUTOFCONTEXT),
            NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_MINIMIZESTART, NativeMethods.EVENT_SYSTEM_MINIMIZEEND,
                IntPtr.Zero, _proc, processId, 0, NativeMethods.WINEVENT_OUTOFCONTEXT),
            NativeMethods.SetWinEventHook(NativeMethods.EVENT_OBJECT_LOCATIONCHANGE, NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero, _proc, processId, 0, NativeMethods.WINEVENT_OUTOFCONTEXT),
        ];
    }

    private void OnEvent(IntPtr hook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint thread, uint time)
    {
        // ウィンドウそのもののイベントだけを見る（カーソルやキャレットなどは除く）
        if (hWnd == IntPtr.Zero || idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF)
        {
            return;
        }
        switch (eventType)
        {
            case NativeMethods.EVENT_SYSTEM_FOREGROUND:
                Foreground?.Invoke(hWnd);
                break;
            case NativeMethods.EVENT_SYSTEM_MOVESIZESTART:
                MoveSizeStarted?.Invoke(hWnd);
                break;
            case NativeMethods.EVENT_SYSTEM_MOVESIZEEND:
                MoveSizeEnded?.Invoke(hWnd);
                break;
            case NativeMethods.EVENT_SYSTEM_MINIMIZESTART:
                MinimizeStarted?.Invoke(hWnd);
                break;
            case NativeMethods.EVENT_SYSTEM_MINIMIZEEND:
                MinimizeEnded?.Invoke(hWnd);
                break;
            case NativeMethods.EVENT_OBJECT_LOCATIONCHANGE:
                LocationChanged?.Invoke(hWnd);
                break;
        }
    }

    public void Dispose()
    {
        foreach (var hook in _hooksByProcess.Values.SelectMany(h => h))
        {
            NativeMethods.UnhookWinEvent(hook);
        }
        _hooksByProcess.Clear();
    }
}
