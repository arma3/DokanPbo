using DokanNet;
using System;

namespace DokanPbo
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            try
            {
                ArchiveManager archiveManager = new ArchiveManager(new String[] {
                    "D:\\SteamLibrary\\steamapps\\common\\Arma 3\\Addons\\"
                });
                PboFS pboFS = new PboFS(archiveManager);
                pboFS.Mount("r:\\", DokanOptions.DebugMode | DokanOptions.StderrOutput);
                Console.WriteLine("Success");
            }
            catch (DokanException ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }
}
