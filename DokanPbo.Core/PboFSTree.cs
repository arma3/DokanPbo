using System;
using SwiftPbo;
using DokanNet;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DokanPbo
{
    public class PboFSTree
    {
        private ArchiveManager archiveManager;
        private PboFsRealFolder root;
        private Dictionary<string, IPboFsNode> fileTreeLookup;

        //The directory all non-virtual files are stored in.
        public readonly string writeableDirectory;
        public readonly string excludePrefix;

        public PboFSTree(ArchiveManager archiveManager, string writeableDirectory, string excludePrefix)
        {
            this.archiveManager = archiveManager;
            this.writeableDirectory = writeableDirectory;
            this.excludePrefix = excludePrefix;

            Console.WriteLine("DokanPbo writeableDirectory is " + writeableDirectory);

            CreateFileTree();
        }

        public IList<FileInformation> FilesForPath(string path)
        {
            var node = this.fileTreeLookup[path];
            if (node is PboFsFolder folder)
            {
                return folder.Children.Values.Select(f => f.FileInformation).ToList();
            }

            return null;
        }

        public FileInformation FileInfoForPath(string path)
        {
            return this.fileTreeLookup.TryGetValue(path, out var node) ? node.FileInformation : new FileInformation();
        }

        public IPboFsNode NodeForPath(string path)
        {
            return this.fileTreeLookup.TryGetValue(path, out var node) ? node : null;
        }

        private List<string> GetFolderPathElements(PboFsFolder folder)
        {
            var output = new List<string>();
            PboFsFolder currentNode = folder;
            output.Add(currentNode.FileInformation.FileName);
            while (currentNode.parent != null)
            {
                currentNode = currentNode.parent;
                output.Add(currentNode.FileInformation.FileName ?? "\\"); //root node has null FileName
            }

            output.Reverse();
            return output;
        }


        public PboFsRealFolder MakeDirectoryWriteable(PboFsFolder folder)
        {
            List<string> pathElements = GetFolderPathElements(folder);

            pathElements.Remove("\\"); //root node is already writeable
            string currentPath = "";
            foreach (var element in pathElements)
            {
                currentPath += "\\" + element;

                var currentDirectoryNode = NodeForPath(currentPath) as PboFsFolder;
                //if (currentDirectoryNode == null) Debugger.Break();

                string currentDirectoryName = currentDirectoryNode.FileInformation.FileName;
                //if (currentDirectoryName != element) Debugger.Break();

                if (currentDirectoryNode is PboFsRealFolder) continue; //already writeable

                try
                {
                    if (!Directory.Exists(writeableDirectory + currentPath))
                        Directory.CreateDirectory(writeableDirectory + currentPath);


                    var folderR = new PboFsRealFolder(currentDirectoryName, writeableDirectory + currentPath, currentDirectoryNode.parent);
                    folderR.Children = currentDirectoryNode.Children;
                    folderR.parent.Children[currentDirectoryName.ToLower()] = folderR;
                    fileTreeLookup[currentPath.ToLower()] = folderR;
                    //Done.

                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    Console.WriteLine(e.Message);
                }

            }

            return NodeForPath(currentPath) as PboFsRealFolder;
        }




        private void CreateFileTree()
        {
            this.root = new PboFsRealFolder(null, writeableDirectory, null);
            this.fileTreeLookup = new Dictionary<string, IPboFsNode>();
            this.fileTreeLookup["\\"] = this.root;
            var hasCfgConvert = PboFS.HasCfgConvert();

            foreach (string filePath in this.archiveManager.FilePathToFileEntry.Keys)
            {
                if (excludePrefix != null && filePath.StartsWith(excludePrefix)) continue;
                FileEntry file = this.archiveManager.FilePathToFileEntry[filePath];

                PboFsFolder currentFolder = root;
                var currentPath = "\\";
                var splitPath = filePath.Split('\\');

                // Create folder for all sub paths
                for (int i = 1; i < splitPath.Length - 1; i++)
                {
                    var folderName = splitPath[i];
                    currentPath += folderName;

                    PboFsFolder folder = null;
                    if (!this.fileTreeLookup.ContainsKey(currentPath))
                    {
                        this.fileTreeLookup[currentPath] = folder = new PboFsFolder(folderName, currentFolder);
                    }
                    else
                    {
                        folder = (PboFsFolder) this.fileTreeLookup[currentPath];
                    }

                    if (!currentFolder.Children.ContainsKey(folderName))
                    {
                        currentFolder.Children[folderName] = currentFolder = folder;
                    }
                    else
                    {
                        currentFolder = (PboFsFolder)currentFolder.Children[folderName];
                    }

                    currentPath += "\\";
                }

                var fileName = splitPath[splitPath.Length - 1];
                var fileNode = new PboFsFile(fileName, file);
                currentFolder.Children[fileName] = fileNode;
                this.fileTreeLookup[filePath] = fileNode;
                if (hasCfgConvert && fileName == "config.bin")
                {
                    var derapNode = new PboFsDebinarizedFile("config.cpp", file);
                    currentFolder.Children["config.cpp"] = derapNode;
                    this.fileTreeLookup[filePath.Replace("config.bin", "config.cpp")] = derapNode;
                }
            }

            //Interweave writeableDirectory
            LinkRealDirectory(new System.IO.DirectoryInfo(writeableDirectory), "", this.root, true);
        }

        void injectFile(FileInfo file, PboFsFolder rootDirectory, string fileFullRealPath)
        {
            if (fileTreeLookup.ContainsKey(fileFullRealPath))
            {
                Console.WriteLine("DokanPbo::LinkRealDirectory cannot add file. It already exists. " + fileFullRealPath);
                return;
            }

            var newFile = new PboFsRealFile(file, rootDirectory);
            rootDirectory.Children[file.Name.ToLower()] = newFile;
            this.fileTreeLookup[fileFullRealPath] = newFile;
        }

        void LinkRealDirectory(System.IO.DirectoryInfo root, string currentPath, PboFsFolder rootDir, bool first)
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
                    Console.WriteLine(e);
                }
                catch (System.IO.DirectoryNotFoundException e)
                {
                    Console.WriteLine(e);
                }

                if (files == null) return;


                foreach (var fi in files)
                    injectFile(fi, rootDir, currentPath + "\\" + fi.Name.ToLower());

                // Now find all the subdirectories under this directory.
                subDirs = root.GetDirectories();

                foreach (var dirInfo in subDirs)
                {
                    // Resursive call for each subdirectory.
                    LinkRealDirectory(dirInfo, currentPath, rootDir, false);
                }

                return;
            }



            currentPath += "\\" + root.Name.ToLower();

            //Make sure rootDir is writeable
            if (!(rootDir is PboFsRealFolder)) rootDir = MakeDirectoryWriteable(rootDir);
            PboFsRealFolder currentFolder;

            //If folder already exists make it writeable
            if (fileTreeLookup.ContainsKey(currentPath))
            {
                var existingCurrentFolder = fileTreeLookup[currentPath] as PboFsFolder;
                if (existingCurrentFolder is PboFsRealFolder existingCurrentFolderReal)
                    currentFolder = existingCurrentFolderReal;
                else
                    currentFolder = MakeDirectoryWriteable(existingCurrentFolder);
            }
            else
            {
                currentFolder = new PboFsRealFolder(root.Name, root.FullName, rootDir);
                rootDir.Children[root.Name.ToLower()] = currentFolder;
                this.fileTreeLookup[currentPath] = currentFolder;
            }

             



            // First, process all the files directly under this folder
            try
            {
                files = root.GetFiles("*.*");
            }
            // This is thrown if even one of the files requires permissions greater
            // than the application provides.
            catch (UnauthorizedAccessException e)
            {
                Console.WriteLine(e);
            }

            catch (System.IO.DirectoryNotFoundException e)
            {
                Console.WriteLine(e);
            }



            if (files == null) return;

            foreach (var fi in files)
                injectFile(fi, currentFolder, currentPath + "\\" + fi.Name.ToLower());

            // Now find all the subdirectories under this directory.
            subDirs = root.GetDirectories();

            foreach (var dirInfo in subDirs)
            {
                // Resursive call for each subdirectory.
                LinkRealDirectory(dirInfo, currentPath, currentFolder, false);
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
