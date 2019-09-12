using System;
using System.Reflection;
using System.Resources;

#if DEBUG

[assembly: AssemblyConfiguration("Debug")]
#else

[assembly: AssemblyConfiguration("Release")]
#endif

[assembly: AssemblyCompany("Cengiz Ilerler")]
[assembly: AssemblyCopyright("Copyright ©  2015")]
[assembly: AssemblyTrademark("Cengiz Ilerler")] // ® ™ ©
[assembly: AssemblyProduct("Ruya .Net Framework Wrapper")]
[assembly: AssemblyInformationalVersion("0.0.0.1")] // Product Version

[assembly: NeutralResourcesLanguage("en-US")]
[assembly: CLSCompliant(false)]