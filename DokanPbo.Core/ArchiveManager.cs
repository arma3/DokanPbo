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

        public ConcurrentDictionary<string, FileEntry> FilePathToFileEntry { get; internal set; }

        public long TotalBytes { get; internal set; }

        public ArchiveManager(string[] folderPaths)
        {
            FilePathToFileEntry = new ConcurrentDictionary<string, FileEntry>();
            TotalBytes = 0;

            var pboList = folderPaths
                //.AsParallel()
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
                .SelectMany(x => x);
            ReadPboFiles(pboList);
        }

        private void ReadPboFiles(IEnumerable<string> filePaths)
        {
            TotalBytes =
                filePaths
                    .AsParallel()
                    .Sum(filePath =>
                    {
                        var archive = new PboArchive(filePath);

                        long fileSize = 0;
                        var prefix = "";
                        if (!string.IsNullOrEmpty(archive.ProductEntry.Prefix))
                        {
                            prefix = "\\" + archive.ProductEntry.Prefix;
                        }

                        foreach (var file in archive.Files)
                        {
                            var wholeFilePath = (prefix + "\\" + file.FileName).ToLower();
                            FilePathToFileEntry[wholeFilePath] = file;
                            fileSize += (long) file.DataSize;
                        }

                        return fileSize;
                    });
        }
    }
}
