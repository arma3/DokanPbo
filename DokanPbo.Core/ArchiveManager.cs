using SwiftPbo;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DokanPbo
{
    public class ArchiveManager
    {

        public IEnumerable<(string, FileEntry)> Enumerator { get; internal set; }

        public long TotalBytes { get; set; }

        public ArchiveManager(string[] folderPaths)
        {
            TotalBytes = 0;

            Enumerator = folderPaths
                .Select(folderPath =>
                {
                    try
                    {
                        return Directory.GetFiles(folderPath, "*.pbo");
                    }
                    catch (DirectoryNotFoundException e)
                    {
                        Console.WriteLine("DokanPBO::ArchiveManager errored due to DirectoryNotFoundException: " + e);
                        return new string[0];
                    }
                })
                .SelectMany(x => x) //Get flat list of pbo file paths
                .AsParallel()
                .Select(filePath => //Parse the pbo headers
                {
                    var archive = new PboArchive(filePath);

                    var prefix = "";
                    if (!string.IsNullOrEmpty(archive.ProductEntry.Prefix))
                    {
                        prefix = "\\" + archive.ProductEntry.Prefix;
                    }

                    return archive.Files.Select(file => ((prefix + "\\" + file.FileName).ToLower(), file));
                })
                .SelectMany(x => x); //Turn the arrays of pbo files into a flat array
        }
    }
}
