using System.Windows;

namespace Wtbun;

/// <summary>タブ列から開くダイアログの置き場所。</summary>
public static class DialogPlacement
{
    // タブ列（細い）の下に出すときの、タブ列との間
    private const double OwnerGap = 8;

    /// <summary>
    /// タブ列の真下に出す。タブ列は細く、画面の上端にあることも多いので、タブ列の中央に合わせると上にはみ出てしまう。
    /// 画面の外には出さない。ダイアログの Loaded で呼ぶ（大きさが決まってから）。
    /// </summary>
    public static void PlaceBelowOwner(Window dialog)
    {
        if (dialog.Owner is not { } owner)
        {
            return;
        }
        var left = owner.Left + (owner.ActualWidth - dialog.ActualWidth) / 2;
        var top = owner.Top + owner.ActualHeight + OwnerGap;
        dialog.Left = Math.Max(SystemParameters.VirtualScreenLeft,
            Math.Min(left, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - dialog.ActualWidth));
        dialog.Top = Math.Max(SystemParameters.VirtualScreenTop,
            Math.Min(top, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - dialog.ActualHeight));
    }
}
