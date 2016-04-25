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
                PboFS pboFS = new PboFS("D:\\SteamLibrary\\steamapps\\common\\Arma 3\\Addons\\data_f.pbo");
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
