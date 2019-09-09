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
        private HashSet<IPboFsNode> fileTreeLookup;

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
            this.fileTreeLookup.TryGetValue(new PboFsLookupDummy(path), out var node);
            if (node is PboFsFolder folder)
            {
                return folder.Children.Values.Select(f => f.FileInformation).ToList();
            }

            return null;
        }

        public FileInformation FileInfoForPath(string path)
        {
            return this.fileTreeLookup.TryGetValue(new PboFsLookupDummy(path), out var node) ? node.FileInformation : new FileInformation();
        }

        public IPboFsNode NodeForPath(string path)
        {
            return this.fileTreeLookup.TryGetValue(new PboFsLookupDummy(path.ToLower()), out var node) ? node : null;
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


        public PboFsRealFolder MakeDirectoryWriteable(PboFsFolder inputFolder)
        {
            List<string> pathElements = GetFolderPathElements(inputFolder);

            pathElements.Remove("\\"); //root node is already writeable
            string currentPath = "";
            foreach (var element in pathElements)
            {
                currentPath += "\\" + element;

                var currentDirectoryNode = NodeForPath(currentPath) as PboFsFolder;

                string currentDirectoryName = currentDirectoryNode.FileInformation.FileName;

                if (currentDirectoryNode is PboFsRealFolder) continue; //already writeable

                try
                {
                    if (!Directory.Exists(writeableDirectory + currentPath))
                        Directory.CreateDirectory(writeableDirectory + currentPath);


                    var folderR = new PboFsRealFolder(currentDirectoryName, writeableDirectory + currentPath, currentDirectoryNode.parent);
                    folderR.Children = currentDirectoryNode.Children;
                    folderR.parent.Children[currentDirectoryName.ToLower()] = folderR;
                    fileTreeLookup.Remove(currentDirectoryNode);
                    fileTreeLookup.Add(folderR);
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
            this.fileTreeLookup = new HashSet<IPboFsNode>();
            this.fileTreeLookup.Add(this.root);
            var hasCfgConvert = PboFS.HasCfgConvert();


            foreach (var (filePath, file) in this.archiveManager.Enumerator)
            {
                if (excludePrefix != null && filePath.StartsWith(excludePrefix)) continue;
                this.archiveManager.TotalBytes += (long)file.DataSize;

                PboFsFolder currentFolder = root;
                var currentPath = "\\";
                var splitPath = filePath.Split(PboFsLookupDummy.PathChars, StringSplitOptions.RemoveEmptyEntries);

                // Make sure the files directory path exists
                for (int i = 0; i < splitPath.Length - 1; i++)
                {
                    var folderName = splitPath[i];
                    currentPath += folderName;

                    //A part of the path might already exist, walking the tree directly via this shortcut saves alot of time
                    currentFolder.Children.TryGetValue(folderName, out var subFolderNode);
                    if (subFolderNode is PboFsFolder subFolder)
                    {
                        currentFolder = subFolder;
                        currentPath += "\\";
                        continue;
                    }
                    var lookup = new PboFsLookupDummy(currentPath);

                    PboFsFolder folder = null;
                    if (!this.fileTreeLookup.Contains(lookup))
                    {
                        folder = new PboFsFolder(folderName, currentFolder);
                        this.fileTreeLookup.Add(folder);
                    }
                    else
                    {
                        fileTreeLookup.TryGetValue(lookup, out var node);
                        folder = node as PboFsFolder;
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
                var fileNode = new PboFsFile(fileName, file, currentFolder);
                currentFolder.Children[fileName] = fileNode;
                this.fileTreeLookup.Add(fileNode);
                if (hasCfgConvert && fileName == "config.bin")
                {
                    var derapNode = new PboFsDebinarizedFile("config.cpp", file, currentFolder);
                    currentFolder.Children["config.cpp"] = derapNode;
                    this.fileTreeLookup.Add(derapNode);
                }
            }

            //Interweave writeableDirectory
            LinkRealDirectory(new System.IO.DirectoryInfo(writeableDirectory), "", this.root, true);
        }

        void injectFile(FileInfo file, PboFsFolder rootDirectory, string fileFullRealPath)
        {
            if (fileTreeLookup.Contains(new PboFsLookupDummy(fileFullRealPath)))
            {
                //file from writeable directory overrides pbo file.
                Console.WriteLine("DokanPbo::LinkRealDirectory overwriting file from PBO with real file: " + fileFullRealPath);
                fileTreeLookup.Remove(new PboFsLookupDummy(fileFullRealPath));
            }

            var newFile = new PboFsRealFile(file, rootDirectory);
            rootDirectory.Children[file.Name.ToLower()] = newFile;
            this.fileTreeLookup.Add(newFile);
        }

        void LinkRealDirectory(System.IO.DirectoryInfo root, string currentPath, PboFsFolder rootDir, bool first)
        {
            System.IO.FileInfo[] files = null;
            System.IO.DirectoryInfo[] subDirs = null;

            if (first)
            {
                // First, process all the files directly under this inputFolder
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

            //If inputFolder already exists make it writeable
            fileTreeLookup.TryGetValue(new PboFsLookupDummy(currentPath), out var existingNode);
            if (existingNode != null)
            {
                var existingCurrentFolder = existingNode as PboFsFolder;
                if (existingCurrentFolder is PboFsRealFolder existingCurrentFolderReal)
                    currentFolder = existingCurrentFolderReal;
                else
                    currentFolder = MakeDirectoryWriteable(existingCurrentFolder);
            }
            else
            {
                currentFolder = new PboFsRealFolder(root.Name, root.FullName, rootDir);
                rootDir.Children[root.Name.ToLower()] = currentFolder;
                this.fileTreeLookup.Add(currentFolder);
            }

             



            // First, process all the files directly under this inputFolder
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
            fileTreeLookup.Remove(new PboFsLookupDummy(filename));
        }

        public void DeleteNode(IPboFsNode node)
        {
            fileTreeLookup.Remove(node);
        }

        public void AddNode(IPboFsNode node)
        {
            fileTreeLookup.Add(node);
        }
    }
}
