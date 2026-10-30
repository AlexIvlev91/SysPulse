namespace SysPulse;

/// <summary>
/// One SysPulse per sign-in session. Launching it again (Start menu, startup
/// entry) brings the running instance's window forward instead.
/// </summary>
internal static class SingleInstance
{
    private const string MutexName = @"Local\SysPulse.Instance";
    private const string EventName = @"Local\SysPulse.Show";

    private static Mutex? _mutex;
    private static EventWaitHandle? _show;

    /// <summary>
    /// Takes the instance lock, waiting up to <paramref name="waitMs"/> for a
    /// previous instance to exit (used when restarting as administrator).
    /// </summary>
    public static bool TryAcquire(int waitMs)
    {
        try
        {
            _mutex = new Mutex(false, MutexName);
            bool owned;
            try
            {
                owned = _mutex.WaitOne(waitMs);
            }
            catch (AbandonedMutexException)
            {
                owned = true; // previous instance crashed
            }
            if (!owned)
            {
                _mutex.Dispose();
                _mutex = null;
            }
            return owned;
        }
        catch (UnauthorizedAccessException)
        {
            return false; // held by an elevated instance
        }
    }

    public static void SignalExisting()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(EventName, out var handle))
            {
                using (handle)
                    handle.Set();
                return;
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        // An elevated instance's event can't be signalled from a normal process.
        MessageBox.Show("SysPulse is already running as administrator. Open it from its tray icon.", "SysPulse",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public static void ListenForActivation(Action onShow)
    {
        _show = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        var thread = new Thread(() =>
        {
            try
            {
                while (_show?.WaitOne() == true)
                    onShow();
            }
            catch (ObjectDisposedException)
            {
            }
        })
        { IsBackground = true, Name = "SysPulse activation" };
        thread.Start();
    }

    public static void Release()
    {
        try { _mutex?.ReleaseMutex(); }
        catch (ApplicationException) { }
        _mutex?.Dispose();
        _mutex = null;
    }
}
