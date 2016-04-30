using SwiftPbo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DokanPbo
{
    class ArchiveManager
    {

        public Dictionary<string, PboArchive> FilePathToArchive { get; internal set; }
        public Dictionary<string, FileEntry> FilePathToFileEntry { get; internal set; }

        public long TotalBytes { get; internal set; }

        public ArchiveManager(string[] folderPaths)
        {
            FilePathToArchive = new Dictionary<string, PboArchive>();
            FilePathToFileEntry = new Dictionary<string, FileEntry>();
            TotalBytes = 0;

            foreach (var folderPath in folderPaths)
            {
                ReadPboFiles(Directory.GetFiles(folderPath, "*.pbo"));
            }
        }

        public Stream ReadStream(string filePath)
        {
            PboArchive archive = null;

            if (FilePathToArchive.TryGetValue(filePath, out archive))
            {
                FileEntry file = null;

                if (FilePathToFileEntry.TryGetValue(filePath, out file))
                {
                    return archive.Extract(file);
                }
            }

            return null;
        }

        private void ReadPboFiles(string[] filePaths)
        {
            foreach(var filePath in filePaths)
            {
                var archive = new PboArchive(filePath);

                foreach (var file in archive.Files)
                {
                    var wholeFilePath = ("\\" + archive.ProductEntry.Prefix + "\\" + file.FileName).ToLower();
                    FilePathToArchive[wholeFilePath] = archive;
                    FilePathToFileEntry[wholeFilePath] = file;
                    TotalBytes += (long) file.DataSize;
                }
            }
        }
    }
}
