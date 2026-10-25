using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MechKit.Core
{
    // No SW/COM calls or UI callbacks on the writer/watchdog thread.
    internal sealed class OperationTrace : IMessageFilter, IDisposable
    {
        private static OperationTrace _current;
        internal static string CurrentFile { get { var current = _current; return current == null ? null : current._file; } }
        private readonly BlockingCollection<string> _queue = new BlockingCollection<string>(4096);
        private readonly ConcurrentDictionary<long, string> _active = new ConcurrentDictionary<long, string>();
        private readonly System.Windows.Forms.Timer _heartbeat;
        private readonly Thread _writer;
        private readonly string _file;
        private long _pulse = Stopwatch.GetTimestamp();
        private long _sequence;
        private int _dropped;
        private int _disposed;
        private string _last = "startup";

        private OperationTrace()
        {
            _file = Path.Combine(AppPaths.Logs, "Operations-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Process.GetCurrentProcess().Id + ".log");
            _heartbeat = new System.Windows.Forms.Timer { Interval = 1000 };
            _heartbeat.Tick += delegate { Interlocked.Exchange(ref _pulse, Stopwatch.GetTimestamp()); };
            _writer = new Thread(WriteLoop) { IsBackground = true, Name = "MechKit diagnostics" };
            _writer.Start();
            Application.AddMessageFilter(this);
            _heartbeat.Start();
            Record("SESSION START assembly=" + typeof(OperationTrace).Assembly.Location);
        }

        internal static void Start()
        {
            try { if (_current == null) _current = new OperationTrace(); }
            catch (Exception ex) { Debug.WriteLine("MechKit recorder startup: " + ex); }
        }

        internal static void Stop()
        {
            var current = _current;
            _current = null;
            if (current != null) current.Dispose();
        }

        internal static void Event(string text)
        {
            var current = _current;
            if (current != null) current.Record(text);
        }

        internal static IDisposable Begin(string name)
        {
            var current = _current;
            return current == null ? (IDisposable)new EmptyScope() : new Scope(current, name);
        }

        private void Record(string text)
        {
            try
            {
                text = (text ?? "").Replace('\r', ' ').Replace('\n', ' ');
                if (text.Length > 2048) text = text.Substring(0, 2048);
                _last = text;
                var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [T" + Thread.CurrentThread.ManagedThreadId + "] " + text;
                if (!_queue.TryAdd(line)) Interlocked.Increment(ref _dropped);
            }
            catch (InvalidOperationException) { }
        }

        public bool PreFilterMessage(ref Message m)
        {
            // Only mouse-down events; never record typed text or keystrokes.
            if (m.Msg == 0x201 || m.Msg == 0x203 || m.Msg == 0x204)
            {
                var control = Control.FromHandle(m.HWnd);
                if (control != null)
                {
                    var form = control.FindForm();
                    if (form != null && form.GetType().Assembly == typeof(OperationTrace).Assembly)
                        Record("INPUT mouse=" + m.Msg + " form=" + form.GetType().Name + " control=" + control.GetType().Name + "/" + control.Name +
                            (control is ButtonBase ? " button=" + control.Text : "") + " point=" + m.LParam.ToInt64());
                }
            }
            return false;
        }

        private void WriteLoop()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Logs);
                using (var stream = new StreamWriter(new FileStream(_file, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8) { AutoFlush = true })
                {
                    bool stalled = false;
                    long lastWarning = 0;
                    while (!_queue.IsCompleted)
                    {
                        string line;
                        if (_queue.TryTake(out line, 1000)) stream.WriteLine(line);
                        var now = Stopwatch.GetTimestamp();
                        var seconds = (now - Interlocked.Read(ref _pulse)) / (double)Stopwatch.Frequency;
                        if (seconds >= 10 && (!stalled || (now - lastWarning) / (double)Stopwatch.Frequency >= 30))
                        {
                            stream.WriteLine(DateTime.Now.ToString("O") + " UI_STALL heartbeatAgeSeconds=" + seconds.ToString("F1") + " last=" + _last + " active=" + string.Join(" | ", _active.Values));
                            stalled = true;
                            lastWarning = now;
                        }
                        else if (seconds < 10 && stalled)
                        {
                            stream.WriteLine(DateTime.Now.ToString("O") + " UI_RECOVERED");
                            stalled = false;
                        }
                        var dropped = Interlocked.Exchange(ref _dropped, 0);
                        if (dropped > 0) stream.WriteLine("DROPPED events=" + dropped);
                    }
                }
                // Keep at most 30 previous sessions. Only touch this recorder's files.
                var files = new DirectoryInfo(AppPaths.Logs).GetFiles("Operations-*.log");
                Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                for (int i = 30; i < files.Length; i++)
                    try { files[i].Delete(); } catch { }
            }
            catch (Exception ex) { Debug.WriteLine("MechKit operation recorder: " + ex); }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Application.RemoveMessageFilter(this);
            _heartbeat.Dispose();
            Record("SESSION STOP");
            _queue.CompleteAdding();
            // Never wait for disk IO on the SW UI thread.
        }

        private sealed class EmptyScope : IDisposable { public void Dispose() { } }
        private sealed class Scope : IDisposable
        {
            private readonly OperationTrace _owner;
            private readonly long _id;
            private readonly Stopwatch _watch = Stopwatch.StartNew();
            private readonly string _name;
            private int _closed;
            internal Scope(OperationTrace owner, string name)
            {
                _owner = owner; _name = name;
                _id = Interlocked.Increment(ref owner._sequence);
                owner._active[_id] = _id + ":" + name;
                owner.Record("BEGIN #" + _id + " " + name);
            }
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _closed, 1) != 0) return;
                _owner.Record("END #" + _id + " " + _name + " elapsedMs=" + _watch.ElapsedMilliseconds);
                string ignored;
                _owner._active.TryRemove(_id, out ignored);
            }
        }
    }
}
