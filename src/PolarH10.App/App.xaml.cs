using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace PolarH10.App;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\TillH.PolarH10.App";
    private const string PrimaryWindowTitleMarker = "Polar H10";
    private const int SwRestore = 9;

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: false, SingleInstanceMutexName);
        try
        {
            _ownsSingleInstanceMutex = _singleInstanceMutex.WaitOne(0, false);
        }
        catch (AbandonedMutexException)
        {
            _ownsSingleInstanceMutex = true;
        }

        if (!_ownsSingleInstanceMutex)
        {
            BringExistingInstanceToFront();
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        if (_singleInstanceMutex is not null)
        {
            if (_ownsSingleInstanceMutex)
                _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            _ownsSingleInstanceMutex = false;
        }

        base.OnExit(e);
    }

    private static void BringExistingInstanceToFront()
    {
        Process? existing = FindExistingInstance();

        if (existing is null)
            return;

        for (int attempt = 0; attempt < 10; attempt++)
        {
            existing.Refresh();
            IntPtr handle = existing.MainWindowHandle;
            if (handle != IntPtr.Zero)
            {
                ShowWindowAsync(handle, SwRestore);
                SetForegroundWindow(handle);
                return;
            }

            Thread.Sleep(100);
        }
    }

    private static Process? FindExistingInstance()
    {
        Process current = Process.GetCurrentProcess();
        string[] candidateNames =
        [
            current.ProcessName,
            "PolarH10.App",
            "Polar H10 App",
        ];

        return Process
            .GetProcesses()
            .Where(process => process.Id != current.Id)
            .Where(process => IsMatchingInstance(process, candidateNames))
            .OrderBy(SafeGetStartTime)
            .FirstOrDefault();
    }

    private static bool IsMatchingInstance(Process process, string[] candidateNames)
    {
        try
        {
            return candidateNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase) ||
                   process.MainWindowTitle.Contains(PrimaryWindowTitleMarker, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static DateTime SafeGetStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch
        {
            return DateTime.MaxValue;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
