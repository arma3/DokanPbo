using System;
using SwiftPbo;
using DokanNet;
using System.Collections.Generic;
using System.Linq;

namespace DokanPbo
{
    class PboFSTree
    {
        private ArchiveManager archiveManager;
        private PboFSFolder root;

        public PboFSTree(ArchiveManager archiveManager)
        {
            this.archiveManager = archiveManager;

            createFileTree();
        }

        public IList<FileInformation> FilesForPath(string path)
        {
            if (path == "\\")
            {
                return this.root.Children.Values.Select(f => f.FileInformation).ToList();
            }

            var currentFolder = this.root;
            var paths = path.Split('\\');

            for (int i = 1; i < paths.Length; i++)
            {
                var folderName = paths[i];
                currentFolder = (PboFSFolder) currentFolder.Children[folderName];
            }

            return currentFolder.Children.Values.Select(f => f.FileInformation).ToList();
        }

        public FileInformation FileInfoForPath(string path)
        {
            if (path == "\\")
            {
                return this.root.FileInformation;
            }

            var currentFolder = this.root;
            var paths = path.Split('\\');

            for (int i = 1; i < paths.Length; i++)
            {
                var folderName = paths[i];
                currentFolder = (PboFSFolder)currentFolder.Children[folderName];
            }

            var fileName = paths[paths.Length - 1];
            return currentFolder.Children[fileName].FileInformation;
        }

        private void createFileTree()
        {
            this.root = new PboFSFolder(null);

            foreach (string filePath in this.archiveManager.FilePathToArchive.Keys)
            {
                PboArchive archive = this.archiveManager.FilePathToArchive[filePath];
                FileEntry file = this.archiveManager.FilePathToFileEntry[filePath];

                PboFSFolder currentFolder = root;
                var paths = filePath.Split('\\');

                // Create folder for all sub paths
                for (int i = 1; i < paths.Length - 1; i++)
                {
                    var folderName = paths[i];

                    if (!currentFolder.Children.ContainsKey(folderName)) {
                        currentFolder.Children[folderName] = new PboFSFolder(folderName);
                    }

                    currentFolder = (PboFSFolder) currentFolder.Children[folderName];
                }

                var fileName = paths[paths.Length - 1];
                currentFolder.Children[fileName] = new PboFSFile(fileName, archive, file);
            }
        }
    }
}
