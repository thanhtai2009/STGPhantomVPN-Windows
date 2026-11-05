using System;
using System.Threading;

namespace STGPhantomVPN.Core
{
    /// <summary>
    /// A running openvpn.exe as seen by the controller. openvpn.exe started by the OpenVPN Interactive
    /// Service has a DACL that grants this user only SYNCHRONIZE, query, VM read and terminate rights,
    /// so System.Diagnostics.Process (which asks for PROCESS_ALL_ACCESS for exit events) cannot be used
    /// for it. Implementations never throw from these members; failures are reported through
    /// <see cref="Error"/> and the process is then treated as still running.
    /// </summary>
    public abstract class OpenVpnProcess : IDisposable
    {
        int _exitRaised;
        int _watching;
        protected volatile bool Disposed;

        public abstract int Id { get; }

        /// <summary>True once the process has terminated.</summary>
        public abstract bool HasExited { get; }

        /// <summary>Waits up to <paramref name="milliseconds"/>; true if the process has exited.</summary>
        public abstract bool WaitForExit(int milliseconds);

        /// <summary>Raised once, on a background thread, after <see cref="StartWatching"/> when the process exits.</summary>
        public event Action Exited;

        /// <summary>Receives unexpected errors (for logging); never required.</summary>
        public Action<string, Exception> Error { get; set; }

        /// <summary>Starts exit monitoring; call after subscribing to <see cref="Exited"/>. Raises it at once if already exited.</summary>
        public void StartWatching()
        {
            if (Disposed || Interlocked.Exchange(ref _watching, 1) == 1) return;
            try { BeginWatch(); }
            catch (Exception ex) { Report("Watching openvpn.exe for exit failed", ex); }
            if (HasExited) RaiseExited();
        }

        /// <summary>Forcibly ends the process if possible and waits up to <paramref name="waitMilliseconds"/>; true if it is gone.</summary>
        public bool TryKill(int waitMilliseconds)
        {
            if (HasExited) return true;
            try
            {
                if (!Terminate()) return WaitForExit(0);
            }
            catch (Exception ex)
            {
                Report("Terminating openvpn.exe failed", ex);
                return HasExited;
            }
            return WaitForExit(waitMilliseconds);
        }

        protected abstract void BeginWatch();

        /// <summary>Requests termination; false when it is not permitted or not possible.</summary>
        protected abstract bool Terminate();

        protected void RaiseExited()
        {
            if (Disposed || Interlocked.Exchange(ref _exitRaised, 1) == 1) return;
            try { Exited?.Invoke(); }
            catch (Exception ex) { Report("openvpn.exe exit handler failed", ex); }
        }

        protected void Report(string message, Exception ex)
        {
            try { Error?.Invoke(message, ex); }
            catch (Exception) { }
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            try { DisposeCore(); }
            catch (Exception) { }
        }

        protected virtual void DisposeCore() { }
    }

    /// <summary>
    /// Process tracked through a waitable handle that is signaled when the process ends (a native process
    /// handle opened with SYNCHRONIZE on Windows; any <see cref="WaitHandle"/> in tests). Exit is detected
    /// with <see cref="ThreadPool.RegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object, int, bool)"/>.
    /// </summary>
    public sealed class WaitHandleOpenVpnProcess : OpenVpnProcess
    {
        readonly int _id;
        readonly WaitHandle _exit;
        readonly Func<bool> _terminate;
        readonly object _gate = new object();
        RegisteredWaitHandle _registration;

        /// <param name="terminate">Requests termination; returns false when it is not permitted. May be null.</param>
        public WaitHandleOpenVpnProcess(int id, WaitHandle exitHandle, Func<bool> terminate)
        {
            _id = id;
            _exit = exitHandle ?? throw new ArgumentNullException(nameof(exitHandle));
            _terminate = terminate;
        }

        public override int Id => _id;

        public override bool HasExited => WaitForExit(0);

        public override bool WaitForExit(int milliseconds)
        {
            try { return _exit.WaitOne(milliseconds < 0 ? Timeout.Infinite : milliseconds); }
            catch (ObjectDisposedException) { return true; }
            catch (Exception ex)
            {
                Report("Waiting for openvpn.exe failed", ex);
                return false;
            }
        }

        protected override void BeginWatch()
        {
            lock (_gate)
            {
                if (Disposed) return;
                _registration = ThreadPool.RegisterWaitForSingleObject(_exit, (s, timedOut) => RaiseExited(), null, Timeout.Infinite, true);
            }
        }

        protected override bool Terminate() => _terminate != null && _terminate();

        protected override void DisposeCore()
        {
            lock (_gate)
            {
                _registration?.Unregister(null);
                _registration = null;
            }
            _exit.Dispose();
        }
    }

    /// <summary>
    /// Fallback when no process handle can be opened at all: polls whether the PID is still running.
    /// </summary>
    public sealed class PollingOpenVpnProcess : OpenVpnProcess
    {
        readonly int _id;
        readonly Func<bool> _isRunning;
        readonly Func<bool> _terminate;
        readonly int _pollMilliseconds;
        readonly object _gate = new object();
        Timer _timer;
        volatile bool _exited;

        public PollingOpenVpnProcess(int id, Func<bool> isRunning, Func<bool> terminate, int pollMilliseconds = 1000)
        {
            _id = id;
            _isRunning = isRunning ?? throw new ArgumentNullException(nameof(isRunning));
            _terminate = terminate;
            _pollMilliseconds = Math.Max(10, pollMilliseconds);
        }

        public override int Id => _id;

        public override bool HasExited
        {
            get
            {
                if (_exited) return true;
                try { _exited = !_isRunning(); }
                catch (Exception ex) { Report("Checking openvpn.exe failed", ex); }
                return _exited;
            }
        }

        public override bool WaitForExit(int milliseconds)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds < 0 ? int.MaxValue : milliseconds);
            while (!HasExited)
            {
                if (Disposed || DateTime.UtcNow >= deadline) return HasExited;
                Thread.Sleep(Math.Min(_pollMilliseconds, 100));
            }
            return true;
        }

        protected override void BeginWatch()
        {
            lock (_gate)
            {
                if (Disposed) return;
                _timer = new Timer(_ => Poll(), null, _pollMilliseconds, _pollMilliseconds);
            }
        }

        void Poll()
        {
            if (!HasExited) return;
            lock (_gate)
            {
                _timer?.Dispose();
                _timer = null;
            }
            RaiseExited();
        }

        protected override bool Terminate() => _terminate != null && _terminate();

        protected override void DisposeCore()
        {
            lock (_gate)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }
    }
}
