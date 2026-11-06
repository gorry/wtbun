using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Wtbun.Native;

/// <summary>
/// クリップボードに文字列を置く。
/// WPF の Clipboard.SetText は、中身を後から渡す形（OLE の遅延レンダリング）で登録してから、すぐに確定（OleFlushClipboard）
/// しようとする。クリップボードを見張っているアプリ（UltraVNC の vncviewer など）が、その間に中身を読もうとして
/// クリップボードを開いたまま wtbun の返事を待つと、wtbun は確定のために待っていて返事ができず、お互いに待って失敗する
/// （CLIPBRD_E_CANT_OPEN）。そこで、Win32 の API で文字列をその場で置く（中身を後から渡さないので、待ち合いにならない）。
/// </summary>
public static class ClipboardText
{
    // ほかのアプリがクリップボードを読んでいる短い間を待つ
    private const int RetryCount = 10;
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>文字列を置く。ほかのアプリがクリップボードを開いたままで置けなければ false。</summary>
    /// <param name="owner">クリップボードの持ち主にするウィンドウ。</param>
    public static bool TrySet(string text, IntPtr owner)
    {
        for (var i = 0; i < RetryCount; i++)
        {
            if (NativeMethods.OpenClipboard(owner))
            {
                try
                {
                    return Put(text);
                }
                finally
                {
                    NativeMethods.CloseClipboard();
                }
            }
            Debug.WriteLine($"OpenClipboard failed: {Marshal.GetLastWin32Error()}");
            Thread.Sleep(RetryInterval);
        }
        return false;
    }

    private static bool Put(string text)
    {
        if (!NativeMethods.EmptyClipboard())
        {
            return false;
        }
        // UTF-16 と終わりの 0
        var bytes = (text.Length + 1) * sizeof(char);
        var memory = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (UIntPtr)bytes);
        if (memory == IntPtr.Zero)
        {
            return false;
        }
        var pointer = NativeMethods.GlobalLock(memory);
        if (pointer == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(memory);
            return false;
        }
        try
        {
            Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
            Marshal.WriteInt16(pointer, text.Length * sizeof(char), 0);
        }
        finally
        {
            NativeMethods.GlobalUnlock(memory);
        }
        // 置けたら、メモリーはクリップボードのものになる（解放しない）
        if (NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, memory) == IntPtr.Zero)
        {
            Debug.WriteLine($"SetClipboardData failed: {Marshal.GetLastWin32Error()}");
            NativeMethods.GlobalFree(memory);
            return false;
        }
        return true;
    }
}
