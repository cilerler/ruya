using System;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using Ruya.MEF.Calculator.Interfaces;

namespace Ruya.MEF.Host
{
    internal sealed class Extensibility
    {
        [Import(typeof(ICalculator))]
        public ICalculator Calculator;

        public Extensibility()
        {
            //An aggregate catalog that combines multiple catalogs
            var catalog = new AggregateCatalog();

            //Adds all the parts found in the same assembly as the Program class
            catalog.Catalogs.Add(new AssemblyCatalog(typeof(Program).Assembly));
            string directory = Path.Combine(Directory.GetCurrentDirectory() + @"\..\..\..\", @"Ruya.Calculator\bin\Debug");
            catalog.Catalogs.Add(Directory.Exists(directory)
                                     ? new DirectoryCatalog(directory)
                                     : new DirectoryCatalog(Directory.GetCurrentDirectory()));


            //Create the CompositionContainer with the parts in the catalog
            var container = new CompositionContainer(catalog);

            //Fill the imports of this object
            try
            {
                container.ComposeParts(this);
            }
            catch (CompositionException compositionException)
            {
                Console.WriteLine(compositionException.ToString());
            }
        }
    }
}
