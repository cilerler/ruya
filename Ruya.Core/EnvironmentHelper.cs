using System;
using System.Collections.Generic;
using System.Linq;

namespace Ruya.Core
{
    public static class EnvironmentHelper
    {
        // TEST method ReplaceEnvironmentFolders
        // COMMENT method ReplaceEnvironmentFolders
        public static string ReplaceEnvironmentFolders(string input)
        {
            // HARD-CODED constant
            var folders = new Dictionary<string, string>
                                 {
                                     {
                                         "%SYSTEM%", Environment.GetFolderPath(Environment.SpecialFolder.System)
                                     },
                                     {
                                         "%PROGRAMFILES%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                                     },
                                     {
                                         "%PROGRAMFILES(x86)%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                                     }
                                 };
            string output = folders.Aggregate(input, (current, folder) => current.Replace(folder.Key, folder.Value));
            return output;
        }
    }
}
