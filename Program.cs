using System.Diagnostics;

namespace AMWebShell;

internal static class Program
{
    private const string MutexName = "AMWebShell.SingleInstance.V1";
    internal const string ActivateEventName = "AMWebShell.Activate.V1";
    internal const string ExitEventName = "AMWebShell.Exit.V1";

    [STAThread]
    static void Main(string[] args)
    {
        Theme.ConfigurePerformanceAndGpu();

        bool replace = args.Any(a => a.Equals("--replace", StringComparison.OrdinalIgnoreCase));
        if (replace)
        {
            try { using var ex = EventWaitHandle.OpenExisting(ExitEventName); ex.Set(); Thread.Sleep(400); } catch { }
        }

        using var mutex = new Mutex(true, MutexName, out bool isNew);
        if (!isNew)
        {
            // 已在运行（可能隐藏在托盘）：通知已有实例显示窗口
            try { using var ev = EventWaitHandle.OpenExisting(ActivateEventName); ev.Set(); } catch { }
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
