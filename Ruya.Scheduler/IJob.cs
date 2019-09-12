using System;

namespace Ruya.Scheduler
{
    public interface IJob
    {
        string Name { get; set; }
        TimeSpan Interval { set; get; }
        bool RunOnce { get; }
        bool IsRunning { get; }
        bool InProgress { get; }
        bool NotifySkip { get; }
        event EventHandler Started;
        event EventHandler Stopping;
        event EventHandler<SkipEventArgs> Skip;
        int NumberOfStartedEvents { get; }
        int NumberOfStoppingEvents { get; }
        int NumberOfOnSkipEvents {get;}
        Job SetInterval(TimeSpan interval);
        Job SetJob(Action action);
        void StartJob();
        void StopJob();
        void RestartJob();
    }
}