using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Timers;
using Ruya.Diagnostics;
using Timer = System.Timers.Timer;

namespace Ruya.Scheduler
{
#warning Refactor
    public class Job : IJob, IDisposable
    {
        private readonly object _updateLock = new object();
        private int _counter;
        private Action _job;
        private bool _skip;
        private Timer _timer;

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public Job()
        {
            Name = GetType().Name;
            Interval = new TimeSpan(0, 0, 0);
            MaxSkipAllowed = 0;
            _timer = new Timer
                     {
                         AutoReset = true
                     };
        }

        #region IDisposable Members

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region IJob Members

        public string Name { get; set; }
        public event EventHandler Started;
        public event EventHandler Stopping;
        public event EventHandler<SkipEventArgs> Skip;
        public int NumberOfStartedEvents => GetNumberOfEvents(Started);
        public int NumberOfStoppingEvents => GetNumberOfEvents(Stopping);
        public int NumberOfOnSkipEvents => Skip?.GetInvocationList().Length ?? 0;
        public bool RunOnce => Interval.Equals(new TimeSpan(0, 0, 0));
        public bool IsRunning { private set; get; }
        public bool InProgress { private set; get; }
        public bool NotifySkip => NumberOfOnSkipEvents > 0;
        public TimeSpan Interval { set; get; }
        public int MaxSkipAllowed { set; get; }
        public Job SetInterval(TimeSpan interval)
        {
            Interval = interval;
            return this;
        }

        public Job SetJob(Action action)
        {
            _job = action;
            return this;
        }

        public void StartJob()
        {
            Tracer.Instance.TraceEvent(TraceEventType.Start, 0, Name);
            if (Interval.TotalMilliseconds > 0)
            {
                _timer.Interval = Interval.TotalMilliseconds;
            }
            _timer.Elapsed += Timer_Elapsed;
            _timer.Start();
            _counter = 0;
            IsRunning = true;
            InProgress = false;
            OnStarted();
        }

        public void StopJob()
        {
            OnStopping();
            IsRunning = false;
            InProgress = false;
            _skip = false; 
            _timer.Stop();
            _timer.Dispose();
            _timer = null;
            Tracer.Instance.TraceEvent(TraceEventType.Stop, 0, Name);
        }

        public void RestartJob()
        {
            StopJob();
            _timer = new Timer
                     {
                         AutoReset = true
                     };
            StartJob();
        }

        #endregion

        private static int GetNumberOfEvents(EventHandler eventHandler)
        {
            return eventHandler?.GetInvocationList()
                                .Length ?? 0;
        }

        public Job SetName(string name)
        {
            Name = name;
            return this;
        }

        public Job SetMaxSkipAllowed(int maxSkipAllowed)
        {
            MaxSkipAllowed = maxSkipAllowed;
            return this;
        }

        protected virtual void OnStarted()
        {
            Started?.Invoke(this, EventArgs.Empty);
        }

        protected virtual void OnStopping()
        {
            Stopping?.Invoke(this, EventArgs.Empty);
        }

        protected virtual void OnSkip(SkipEventArgs e)
        {
            Skip?.Invoke(this, e);
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            InProgress = true;
            if (NotifySkip && !RunOnce)
            {
                if (Monitor.TryEnter(_updateLock))
                {
                    if (_skip)
                    {
                        _skip = false;
                        Tracer.Instance.TraceEvent(TraceEventType.Resume, 0, Name);
                    }
                    try
                    {
                        _counter = 0;
                        _job();
                    }
                    finally
                    {
                        Monitor.Exit(_updateLock);
                    }
                }
                else
                {
                    _counter++;
                    if (!_skip)
                    {
                        Tracer.Instance.TraceEvent(TraceEventType.Suspend, 0, Name);
                    }
                    _skip = true;
                    OnSkip(new SkipEventArgs(_counter));
                    bool skipLimitSet = MaxSkipAllowed > 0;
                    bool skipLimitReached = _counter.Equals(MaxSkipAllowed);
                    bool restartJob = skipLimitSet && skipLimitReached;
                    if (restartJob)
                    {                        
                        RestartJob();
                    }
                }
            }
            else
            {
                _timer.Enabled = false;
                _job();
                _timer.Enabled = true;
            }
            InProgress = false;
            if (RunOnce)
            {
                StopJob();
            }
        }        

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                ((IDisposable) _timer).Dispose();
            }
        }
    }
}