using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.ComponentModel.Composition.Primitives;
using Ruya.Composition.Interfaces;

namespace Ruya.Composition
{
#warning Refactor
    public sealed class Extensibility
    {
        [Import(typeof(IExtension))]
        public IExtension Extension { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public Extensibility(ComposablePartCatalog catalog)
        {   
            var container = new CompositionContainer(catalog);

            try
            {
                container.ComposeParts(this);
            }
            catch (System.ComponentModel.Composition.CompositionException compositionException)
            {
                throw new CompositionException(compositionException.ToString());
            }
        }
    }
}