using System;
using System.AddIn.Hosting;

namespace Ruya.MAF.ExceptionHelpers
{
    internal class RemoteExceptionHelper : MarshalByRefObject
    {
        private AddInToken _token;

        internal void Init(AddInToken token)
        {
            _token = token;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            UnhandledExceptionHelper.AddTokenToUnreliableList(_token);
        }

        public override object InitializeLifetimeService()
        {
            return null;
        }
    }
}
