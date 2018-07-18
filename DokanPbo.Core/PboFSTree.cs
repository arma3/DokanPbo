using System;
using SwiftPbo;
using DokanNet;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DokanPbo
{
    public class PboFSTree
    {
        private ArchiveManager archiveManager;
        private PboFSRealFolder root;
        private Dictionary<string, IPboFsNode> fileTreeLookup;

        public PboFSTree(ArchiveManager archiveManager)
        {
            this.archiveManager = archiveManager;

            CreateFileTree();
        }

        public IList<FileInformation> FilesForPath(string path)
        {
            var node = this.fileTreeLookup[path];
            if (node is PboFSFolder folder)
            {
                return folder.Children.Values.Select(f => f.FileInformation).ToList();
            }

            return new FileInformation[0];
        }

        public FileInformation FileInfoForPath(string path)
        {
            IPboFsNode node;
            if (this.fileTreeLookup.TryGetValue(path, out node))
            {
                return node.FileInformation;
            }

            return new FileInformation();
        }

        public IPboFsNode NodeForPath(string path)
        {
            IPboFsNode node = null;
            if (this.fileTreeLookup.TryGetValue(path, out node))
            {
                return node;
            }

            return null;
        }

        private void CreateFileTree()
        {
            this.root = new PboFSRealFolder(null, "X:\\pbos", null); //#TODO use global writefiles folder
            this.fileTreeLookup = new Dictionary<string, IPboFsNode>();
            this.fileTreeLookup["\\"] = this.root;
            var hasCfgConvert = PboFS.HasCfgConvert();

            foreach (string filePath in this.archiveManager.FilePathToFileEntry.Keys)
            {
                FileEntry file = this.archiveManager.FilePathToFileEntry[filePath];

                PboFSFolder currentFolder = root;
                var currentPath = "\\";
                var splitPath = filePath.Split('\\');

                // Create folder for all sub paths
                for (int i = 1; i < splitPath.Length - 1; i++)
                {
                    var folderName = splitPath[i];
                    currentPath += folderName;

                    PboFSFolder folder = null;
                    if (!this.fileTreeLookup.ContainsKey(currentPath))
                    {
                        this.fileTreeLookup[currentPath] = new PboFSFolder(folderName);
                    }

                    folder = (PboFSFolder) this.fileTreeLookup[currentPath];

                    if (!currentFolder.Children.ContainsKey(folderName))
                    {
                        currentFolder.Children[folderName] = folder;
                    }

                    currentFolder = (PboFSFolder) currentFolder.Children[folderName];
                    currentPath += "\\";
                }

                var fileName = splitPath[splitPath.Length - 1];
                var fileNode = new PboFSFile(fileName, file);
                currentFolder.Children[fileName] = fileNode;
                this.fileTreeLookup[filePath] = fileNode;
                if (hasCfgConvert && fileName == "config.bin")
                {
                    var derapNode = new PboFSDebinarizedFile("config.cpp", file);
                    currentFolder.Children["config.cpp"] = derapNode;
                    this.fileTreeLookup[filePath.Replace("config.bin", "config.cpp")] = derapNode;
                }
            }

            var rlFile = new PboFSRealFile(new System.IO.FileInfo("X:\\bintreee.cpp"), this.root);

            this.root.Children["bintreee.cpp"] = rlFile;
            this.fileTreeLookup["\\bintreee.cpp"] = rlFile;



            WalkDirectoryTree(new System.IO.DirectoryInfo("X:\\pbos\\"), "", this.root, true);



        }

        void WalkDirectoryTree(System.IO.DirectoryInfo root, string currentPath, PboFSFolder rootDir, bool first)
        {
            System.IO.FileInfo[] files = null;
            System.IO.DirectoryInfo[] subDirs = null;


            if (first)
            {
                // First, process all the files directly under this folder
                try
                {
                    files = root.GetFiles("*.*");
                }
                // This is thrown if even one of the files requires permissions greater
                // than the application provides.
                catch (UnauthorizedAccessException e)
                {
                    //#TODO you have no permission to access files
                }

                catch (System.IO.DirectoryNotFoundException e)
                {
                    return;
                }

                if (files == null) return;


                foreach (var fi in files)
                {
                    var newFile = new PboFSRealFile(fi, rootDir);
                    rootDir.Children[fi.Name.ToLower()] = newFile;
                    this.fileTreeLookup[currentPath + "\\" + fi.Name.ToLower()] = newFile;
                }

                // Now find all the subdirectories under this directory.
                subDirs = root.GetDirectories();

                foreach (var dirInfo in subDirs)
                {
                    // Resursive call for each subdirectory.
                    WalkDirectoryTree(dirInfo, currentPath, rootDir, false);
                }

                return;
            }


            //#TODO don't create if already exist. Might interleave read only folders
            PboFSRealFolder currentFolder = new PboFSRealFolder(root.Name, root.FullName, rootDir);
            currentPath += "\\" + root.Name.ToLower();



            // First, process all the files directly under this folder
            try
            {
                files = root.GetFiles("*.*");
            }
            // This is thrown if even one of the files requires permissions greater
            // than the application provides.
            catch (UnauthorizedAccessException e)
            {
                //#TODO you have no permission to access files
            }

            catch (System.IO.DirectoryNotFoundException e)
            {
                return;
            }

            rootDir.Children[root.Name.ToLower()] = currentFolder;
            this.fileTreeLookup[currentPath] = currentFolder;

            if (files == null) return;

            foreach (var fi in files)
            {
                var newFile = new PboFSRealFile(fi, currentFolder);
                currentFolder.Children[fi.Name.ToLower()] = newFile;
                this.fileTreeLookup[currentPath + "\\" + fi.Name.ToLower()] = newFile;
            }

            // Now find all the subdirectories under this directory.
            subDirs = root.GetDirectories();

            foreach (var dirInfo in subDirs)
            {
                // Resursive call for each subdirectory.
                WalkDirectoryTree(dirInfo, currentPath, currentFolder, false);
            }
        }

        public void DeleteNode(string filename)
        {
            fileTreeLookup.Remove(filename);
        }

        public void AddNode(string s, IPboFsNode node)
        {
            fileTreeLookup[s] = node;
        }
    }
}
