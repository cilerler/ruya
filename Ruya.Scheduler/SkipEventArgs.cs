using System;

namespace Ruya.Scheduler
{
    public class SkipEventArgs : EventArgs
    {
        public SkipEventArgs(int counter)
        {
            Counter = counter;
        }

        public int Counter { set; get; }
    }
}