using System.ComponentModel;
using System.Diagnostics;
using Wtbun.Config;

namespace Wtbun.Terminal;

public sealed class TerminalLaunchException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Windows Terminal のウィンドウを開き、そのウィンドウハンドルを特定する。
/// </summary>
public static class TerminalLauncher
{
    private static readonly TimeSpan FindTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    // 起動の前後のウィンドウ一覧を比べて特定するので、同時に 2 つ開くと取り違える。1 つずつ開く
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <exception cref="TerminalLaunchException">wt.exe を起動できない、または新しいウィンドウが見つからない。</exception>
    public static async Task<IntPtr> OpenAsync(string windowName, string projectDir, ProjectConfig config, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            // Windows Terminal はすべてのウィンドウを 1 つのプロセスで動かすので、プロセス ID では見分けられない
            var before = TerminalWindows.Enumerate(visibleOnly: false);

            var arguments = WtCommand.Build(windowName, projectDir, config);
            try
            {
                using var _ = Process.Start(new ProcessStartInfo("wt.exe", arguments) { UseShellExecute = false });
            }
            catch (Win32Exception ex)
            {
                throw new TerminalLaunchException($"Failed to start wt.exe {arguments}", ex);
            }

            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < FindTimeout)
            {
                await Task.Delay(PollInterval, cancellationToken);
                foreach (var hWnd in TerminalWindows.Enumerate(visibleOnly: true))
                {
                    if (!before.Contains(hWnd))
                    {
                        return hWnd;
                    }
                }
            }
            throw new TerminalLaunchException($"No new terminal window appeared within {FindTimeout.TotalSeconds}s: wt.exe {arguments}");
        }
        finally
        {
            Gate.Release();
        }
    }
}
