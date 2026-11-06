using Wtbun.Native;

namespace Wtbun.Layout;

/// <summary>
/// 全プロジェクトのウィンドウで共有する位置とサイズ。ウィンドウの上には wtbun のタブ列を置くので、
/// ウィンドウはタブ列の高さぶんの余白を空けて置く。
/// </summary>
public sealed class TerminalLayout(double stripHeightDip)
{
    /// <summary>全ウィンドウの位置とサイズ（GetWindowRect の座標）。まだ決まっていなければ null。</summary>
    public RECT? SharedRect { get; set; }

    // 「最大化」で作業領域いっぱいにする前の位置とサイズ。もう一度最大化するとここへ戻す
    private RECT? _beforeFill;

    public int StripHeight(IntPtr hWnd) => (int)Math.Round(stripHeightDip * WindowOps.GetDpi(hWnd) / 96.0);

    /// <summary>
    /// 利用者が動かしたウィンドウを、タブ列の余白を空けた位置に直し、直した後の位置とサイズを返す。
    /// 最大化されていたら、本当の最大化はやめて、タブ列の下の作業領域いっぱいにする。
    /// </summary>
    public RECT Normalize(IntPtr source)
    {
        if (WindowOps.IsMaximized(source))
        {
            WindowOps.Restore(source, activate: true);
            var fill = FillRect(source);
            RECT target;
            if (_beforeFill is { } before && SharedRect == fill)
            {
                // いっぱいの状態でもう一度最大化されたら、元の大きさに戻す
                target = before;
                _beforeFill = null;
            }
            else
            {
                _beforeFill = SharedRect ?? WindowOps.GetRect(source);
                target = fill;
            }
            WindowOps.Move(source, target);
            return target;
        }

        var rect = WindowOps.GetRect(source);
        var frame = WindowOps.GetFrameBounds(source);
        var work = WindowOps.GetWorkArea(source);
        var minTop = work.Top + StripHeight(source);
        if (frame.Top < minTop)
        {
            var dy = minTop - frame.Top;
            rect = rect.Offset(0, dy);
            var overflow = frame.Bottom + dy - work.Bottom;
            if (overflow > 0)
            {
                rect = rect with { Bottom = Math.Max(rect.Top + 1, rect.Bottom - overflow) };
            }
            WindowOps.Move(source, rect);
        }
        return rect;
    }

    /// <summary>共有の位置とサイズに合わせる。前面には出さない。最小化されているウィンドウはそのままにする。</summary>
    public void Apply(IntPtr hWnd)
    {
        if (SharedRect is not { } shared || WindowOps.IsMinimized(hWnd))
        {
            return;
        }
        if (WindowOps.IsMaximized(hWnd))
        {
            WindowOps.Restore(hWnd, activate: false);
        }
        if (WindowOps.GetRect(hWnd) != shared)
        {
            WindowOps.Move(hWnd, shared);
        }
    }

    /// <summary>ウィンドウの真上に置くタブ列の矩形。</summary>
    public RECT StripRect(IntPtr hWnd)
    {
        var frame = WindowOps.GetFrameBounds(hWnd);
        var height = StripHeight(hWnd);
        return new RECT(frame.Left, frame.Top - height, frame.Right, frame.Top);
    }

    /// <summary>見えている枠が、タブ列の下の作業領域にちょうど収まる位置とサイズ。</summary>
    private RECT FillRect(IntPtr hWnd)
    {
        var rect = WindowOps.GetRect(hWnd);
        var frame = WindowOps.GetFrameBounds(hWnd);
        var work = WindowOps.GetWorkArea(hWnd);
        return new RECT(
            work.Left - (frame.Left - rect.Left),
            work.Top + StripHeight(hWnd) - (frame.Top - rect.Top),
            work.Right + (rect.Right - frame.Right),
            work.Bottom + (rect.Bottom - frame.Bottom));
    }
}
