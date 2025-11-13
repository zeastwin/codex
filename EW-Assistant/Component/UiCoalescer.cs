using System;
using System.Windows.Threading;

internal sealed class UiCoalescer : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly TimeSpan _interval;
    private readonly Action _flush;
    private DispatcherTimer _timer;
    private bool _pending;

    public UiCoalescer(Dispatcher dispatcher, TimeSpan interval, Action flush)
    {
        _dispatcher = dispatcher;
        _interval = interval;
        _flush = flush;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = interval
        };
        _timer.Tick += (s, e) =>
        {
            _timer.Stop();
            _pending = false;
            try { _flush(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[UiCoalescer] flush error: " + ex); }

        };
    }

    public void Request()
    {
        if (_pending) return;
        _pending = true;
        _timer.Stop();
        _timer.Start();
    }

    public void Dispose() => _timer?.Stop();
}
