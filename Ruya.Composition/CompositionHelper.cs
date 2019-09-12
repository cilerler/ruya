using System;
using System.Collections.Generic;
using System.ComponentModel.Composition.Hosting;
using System.ComponentModel.Composition.Primitives;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.ComponentModel.Composition.Diagnostics;

namespace Ruya.Composition
{
    public static class CompositionHelper
    {
        public static AggregateCatalog GetCatalog(string path)
        {
            var catalog = new AggregateCatalog();
            catalog.Catalogs.Add(new AssemblyCatalog(Assembly.GetEntryAssembly()));
            if (!Directory.Exists(path))
            {
                catalog.Catalogs.Add(new DirectoryCatalog(Directory.GetCurrentDirectory()));
                return catalog;
            }
            catalog.Catalogs.Add(new DirectoryCatalog(path));
            foreach (string folder in Directory.EnumerateDirectories(path))
            {
                catalog.Catalogs.Add(new DirectoryCatalog(folder));
            }
            return catalog;
        }
        
        private static CompositionInfo GetCompositionInfo(string path)
        {
            AggregateCatalog catalog = GetCatalog(path);
            var host = new CompositionContainer(catalog);
            var compositionInfo = new CompositionInfo(catalog, host);
            return compositionInfo;
        }

        public static IEnumerable<string> DiscoverExportParts(string path, IList<string> acceptableContractNames)
        {
            var results = new List<string>();
            
            try
            {
                CompositionInfo compositionInfo = GetCompositionInfo(path);
                results.AddRange(from partDefinitionInfo in compositionInfo.PartDefinitions
                                 from exportDefinition in partDefinitionInfo.PartDefinition.ExportDefinitions
                                 let compositionElement = exportDefinition as ICompositionElement
                                 where compositionElement != null
                                 let displayName = compositionElement.Origin?.DisplayName
                                 let contractName = exportDefinition.ContractName
                                 // HARD-CODED constant
                                 let compositionType = exportDefinition.Metadata["ExportTypeIdentity"] as string
                                 // HARD-CODED constant
                                 let compositionName = exportDefinition.Metadata["Name"] as string
                                 where acceptableContractNames.Contains(contractName)
                                 select compositionName);

                /*
                string output;
                using (var stringWriter = new StringWriter())
                {
                    CompositionInfoTextFormatter.Write(compositionInfo, stringWriter);                    
                    output = stringWriter.ToString();
                }
                return output;
                */

            }
            catch (ReflectionTypeLoadException ex)
            {
                foreach (Exception exception in ex.LoaderExceptions)
                {
                    throw new CompositionException(exception.Message);
                }
                throw new CompositionException(ex.Message);
            }
            return results;
        }

    }
}