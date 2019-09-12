using System;
using System.Diagnostics;

namespace Ruya.MEF.Host
{
    internal class Program
    {
        private static void Main()
        {
            Debug.Listeners.Add(new ConsoleTraceListener());
            Debug.WriteLine(Calculate("4+3"));
            Debug.WriteLine(Calculate("4-3"));
            Debug.WriteLine(Calculate("4%3"));
            Debug.WriteLine(Calculate("4*3")); // will throw Operation not found
            Console.ReadLine();
        }

        public static string Calculate(string input)
        {
            return new Extensibility().Calculator.Calculate(input);
        }
    }
}