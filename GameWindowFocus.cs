using System.Runtime.InteropServices;

namespace SCTrainer;

internal static class GameWindowFocus
{
    public static async Task PrepareAsync(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindow(window))
            throw new InvalidOperationException("Game window is not ready.");
        if (IsIconic(window)) ShowWindowAsync(window, 9);
        SetForegroundWindow(window);
        for (int i = 0; i < 20; i++)
        {
            if (GetForegroundWindow() == window && !IsIconic(window))
            {
                // Let the renderer reacquire its device before loading draws progress frames.
                await Task.Delay(350);
                if (GetForegroundWindow() == window) return;
            }
            await Task.Delay(100);
        }
        throw new InvalidOperationException("Could not focus the game safely. No map request was sent. Restore the game, then try again.");
    }

    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
