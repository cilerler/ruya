using System.IO;
using System.IO.MemoryMappedFiles;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using Ruya.Diagnostics;

namespace Ruya.IO
{
    public static class MemoryMappedFileHelper
    {
        public static void Write(string value, string mapName, long capacity, string mutexName, out MemoryMappedFile memoryMappedFile)
        {
            var securityIdentifier = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var security = new MemoryMappedFileSecurity();
            security.AddAccessRule(new AccessRule<MemoryMappedFileRights>(securityIdentifier, MemoryMappedFileRights.FullControl, AccessControlType.Allow));
            memoryMappedFile = MemoryMappedFile.CreateNew(mapName, capacity, MemoryMappedFileAccess.ReadWrite, MemoryMappedFileOptions.None, security, HandleInheritability.Inheritable);
            bool mutexCreated;
            var mutex = new Mutex(true, mutexName, out mutexCreated);
            using (MemoryMappedViewStream stream = memoryMappedFile.CreateViewStream())
            {
                var writer = new BinaryWriter(stream);
                writer.Write(value);                
            }
            mutex.ReleaseMutex();
            // HARD-CODED constant
            Tracer.Instance.TraceEvent(System.Diagnostics.TraceEventType.Verbose, 0, $"Memory-mapped file set to {mapName} as {value}");
        }

        public static string Read(string mapName)
        {
            string output = string.Empty;
            try
            {
                using (MemoryMappedFile mmf = MemoryMappedFile.OpenExisting(mapName))
                {
                    using (MemoryMappedViewStream stream = mmf.CreateViewStream())
                    {
                        var reader = new BinaryReader(stream);
                        string value = reader.ReadString();
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            // HARD-CODED constant
                            Tracer.Instance.TraceEvent(System.Diagnostics.TraceEventType.Verbose, 0, "Memory-mapped file is empty.");
                        }
                        else
                        {
                            // HARD-CODED constant
                            Tracer.Instance.TraceEvent(System.Diagnostics.TraceEventType.Verbose, 0, "Memory-mapped file requested.");
                            output = value;
                        }
                    }
                }
            }
            catch (FileNotFoundException)
            {
                // HARD-CODED constant
                Tracer.Instance.TraceEvent(System.Diagnostics.TraceEventType.Warning, 0, "Memory-mapped file does not exist.");
            }
            return output;
        }
    }
}