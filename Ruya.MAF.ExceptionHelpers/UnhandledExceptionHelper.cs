using System;
using System.AddIn.Hosting;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace Ruya.MAF.ExceptionHelpers
{
    public class UnhandledExceptionHelper
    {
        private const string SCachePath = "unreliableTokens.tokens";

        public static void LogUnhandledExceptions(object addin)
        {
            AddInController controller = AddInController.GetAddInController(addin);
            AddInToken token = controller.Token;
            AppDomain domain = controller.AppDomain;
            var helper = (RemoteExceptionHelper)domain.CreateInstanceFromAndUnwrap(typeof(RemoteExceptionHelper).Assembly.Location, typeof(RemoteExceptionHelper).FullName);
            helper.Init(token);
        }

        internal static void AddTokenToUnreliableList(AddInToken token)
        {
            List<AddInToken> tokens = GetUnreliableTokens();
            tokens.Add(token);
            WriteUnreliableTokens(tokens);
        }

        public static List<AddInToken> GetUnreliableTokens()
        {
            var f = new BinaryFormatter();
            if (File.Exists(SCachePath))
            {
                return (List<AddInToken>)f.Deserialize(File.OpenRead(SCachePath));
            }
            return new List<AddInToken>();
        }

        private static void WriteUnreliableTokens(List<AddInToken> tokens)
        {
            var f = new BinaryFormatter();
            f.Serialize(File.OpenWrite(SCachePath), tokens);
        }
    }
}