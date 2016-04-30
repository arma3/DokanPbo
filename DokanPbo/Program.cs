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
                    "D:\\SteamLibrary\\steamapps\\common\\Arma 3\\Addons\\",
                    "D:\\SteamLibrary\\steamapps\\common\\Arma 3\\Curator\\Addons\\",
                    "D:\\SteamLibrary\\steamapps\\common\\Arma 3\\Heli\\Addons\\",
                    "D:\\SteamLibrary\\steamapps\\common\\Arma 3\\Kart\\Addons\\",
                    "D:\\SteamLibrary\\steamapps\\common\\Arma 3\\Mark\\Addons\\",
                });
                PboFSTree fileTree = new PboFSTree(archiveManager);
                PboFS pboFS = new PboFS(fileTree, archiveManager, "\\a3");
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
