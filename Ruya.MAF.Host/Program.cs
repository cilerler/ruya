using System;
using System.AddIn.Hosting;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Ruya.MAF.ExceptionHelpers;
using Ruya.MAF.Host.AddIns.Calculator;
using Ruya.MAF.Host.AddIns.NumberProcessor;

namespace Ruya.MAF.Host
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Debug.Listeners.Add(new ConsoleTraceListener());            
            new Menu.MenuCollection("MAF", false, DoWorkCalculator, DoWorkNumberProcessor).Run();
        }

        [Description("Number Processor")]
        private static void DoWorkNumberProcessor()
        {
            AddInToken token = GetToken<HostView.NumberProcessor.NumberProcessorHostView>();
            var addIn = ActivateAddIn<HostView.NumberProcessor.NumberProcessorHostView>(token);
            while (RunNumberProcessor(addIn))
            {
                token = GetToken<HostView.NumberProcessor.NumberProcessorHostView>();
                addIn = ActivateAddIn<HostView.NumberProcessor.NumberProcessorHostView>(token);
            }
        }

        [Description("Calculator")]
        private static void DoWorkCalculator()
        {
            AddInToken token = GetToken<HostView.Calculator.Device>();
            var addIn = ActivateAddIn<HostView.Calculator.Device>(token);
            while (RunCalculator(addIn))
            {
                token = GetToken<HostView.Calculator.Device>();
                addIn = ActivateAddIn<HostView.Calculator.Device>(token);
            }
        }

        /// <summary>
        /// Activates an add-in with a full trust level in a new application domain.
        /// </summary>
        /// <returns>
        /// The host view of the add-in.
        /// </returns>
        private static T ActivateAddIn<T>(AddInToken token)
        {

            var addIn = token.Activate<T>(AddInSecurityLevel.FullTrust);
            return addIn;
        }

        private static AddInToken GetToken<T>()
        {
            string addInRoot = Environment.CurrentDirectory;

            //Check to see if new AddIns have been installed
            AddInStore.Rebuild(addInRoot);
            //AddInStore.Update(addInRoot);
            //string[] s = AddInStore.RebuildAddIns(addInRoot);

            //Look for AddIns in our root directory and store the results
            Collection<AddInToken> tokens = AddInStore.FindAddIns(typeof(T), addInRoot);

            //Ask the user which AddIn they would like to use
            AddInToken token = ChooseAddIn(tokens);
            return token;
        }

        private static AddInToken ChooseAddIn(IList<AddInToken> tokens)
        {
            List<AddInToken> unreliableTokens = UnhandledExceptionHelper.GetUnreliableTokens();
            if (tokens.Count == 0)
            {
                Console.WriteLine("No add-in are available");
                return null;
            }
            Console.WriteLine("Available Add-ins: ");
            for (var i = 0; i < tokens.Count; i++)
            {
                var warning = "";
                if (unreliableTokens.Any(token => tokens[i].AssemblyName.FullName.Equals(token.AssemblyName.FullName)))
                {
                    warning = "{ Possibly Unreliable }";
                }

                Console.WriteLine("\t{0}. {1}{2}", (i + 1), tokens[i].Name, warning);
            }
            Console.WriteLine("Which add-in do you want to use?");
            string line = Console.ReadLine();
            int selection;
            if (int.TryParse(line, out selection))
            {
                if (selection <= tokens.Count)
                {
                    return tokens[selection - 1];
                }
            }
            Console.WriteLine("Invalid selection: {0}. Please choose again.", line);
            return ChooseAddIn(tokens);
        }

        private static bool RunCalculator(HostView.Calculator.Device addIn)
        {
            if (addIn == null)
            {
                //No add-in were found, read a line and exit
                Console.ReadLine();
                Environment.Exit(0);
            }
            UnhandledExceptionHelper.LogUnhandledExceptions(addIn);
            Console.WriteLine("Available operations: " + addIn.Operations);
            Console.WriteLine("Type \"exit\" to exit, type \"reload\" to reload");
            string line = Console.ReadLine();
            while (line != null &&
                   !line.Equals("exit"))
            {
                if (line.Equals("reload"))
                {
                    return true;
                }
                //We have a very simple parser, if anything unexpected happens just ask the user to try again.  
                try
                {
                    var c = new Command(line);
                    Console.WriteLine(addIn.Operate(new HostOperate(c.Action, c.A, c.B)));
                }
                catch
                {
                    Console.WriteLine("Invalid command: {0}. Commands must be formated: [number] [operation] [number]", line);
                    Console.WriteLine("Available operations: " + addIn.Operations);
                }

                line = Console.ReadLine();
            }

            return false;
        }
        
        private static bool RunNumberProcessor(HostView.NumberProcessor.NumberProcessorHostView addIn)
        {
            if (addIn == null)
            {
                //No add-in were found, read a line and exit
                Console.ReadLine();
                Environment.Exit(0);
            }
            UnhandledExceptionHelper.LogUnhandledExceptions(addIn);
            Console.WriteLine("Type \"exit\" to exit, type \"reload\" to reload, type anything else to run");
            string line = Console.ReadLine();
            while (line != null &&
                   !line.Equals("exit"))
            {
                if (line.Equals("reload"))
                {
                    return true;
                }

                var automationHost = new AutomationHost(Console.Out);
                addIn.Initialize(automationHost);
                List<int> numbersProcessed = addIn.ProcessNumbers(1, 20);
                Console.WriteLine(string.Join(",", numbersProcessed));                

                line = Console.ReadLine();
            }
            return false;            
        }

    }

}
