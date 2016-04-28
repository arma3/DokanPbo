using DokanNet;
using System;

namespace DokanPbo
{
    internal class Program
    {

#if DEBUG
        static readonly DokanOptions MOUNT_OPTIONS = DokanOptions.DebugMode | DokanOptions.StderrOutput;
#else
        static readonly DokanOptions MOUNT_OPTIONS = DokanOptions.FixedDrive;
#endif

        private static void Main(string[] args)
        {
            try
            {
                ArchiveManager archiveManager = new ArchiveManager(new String[] {
                    "D:\\SteamLibrary\\steamapps\\common\\Arma 3\\Addons\\"
                });
                PboFS pboFS = new PboFS(archiveManager);
                pboFS.Mount("r:\\", Program.MOUNT_OPTIONS);
                Console.WriteLine("Success");
            }
            catch (DokanException ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }
}
