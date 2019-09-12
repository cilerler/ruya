using System;
using System.Diagnostics;
using System.Reflection;
using System.ServiceProcess;
using Ruya.Diagnostics;
using Ruya.Scheduler;

namespace Ruya.Host
{
    public partial class Service1 : ServiceBase
    {
        private readonly Job _job;

        public Service1(string service)
        {
            InitializeComponent();

            ServiceName = service;
            _job = new Job
                   {
                       Name = GetType().Name
                   };

            //HARD-CODED Constant
            Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, $"Service name is {ServiceName}");
        }
        
        protected override void OnStart(string[] args)
        {
            RequestAdditionalTime(Convert.ToInt32(new TimeSpan(0, 1, 0).TotalMilliseconds));
#if DEBUG
            //! uncomment following line only if you need to debug Windows Service
            // Helper.CallDebugger();
#endif
            //HARD-CODED Constant
            Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, $"{ServiceName}.{MethodBase.GetCurrentMethod().Name}");

            _job.Started += Program.Start;
            _job.Stopping += Program.Stop;
            _job.SetJob(() => Program.Run(args)).StartJob();
        }

        protected override void OnStop()
        {
            RequestAdditionalTime(Convert.ToInt32(new TimeSpan(0, 1, 0).TotalMilliseconds));

            //HARD-CODED Constant
            Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, $"{ServiceName}.{MethodBase.GetCurrentMethod().Name}");

            _job.StopJob();
        }        
    }
}
