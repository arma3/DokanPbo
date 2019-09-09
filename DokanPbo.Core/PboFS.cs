using DokanNet;
using Microsoft.Win32;
using SwiftPbo;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using FileAccess = DokanNet.FileAccess;

namespace DokanPbo
{
    public class PboFS : IDokanOperations
    {
        private ArchiveManager archiveManager;
        private PboFSTree fileTree;
        private readonly string prefix;

        //#TODO support for armake and mikero deRap
        static readonly string CFG_CONVERT_PATH = (string)Registry.GetValue("HKEY_CURRENT_USER\\Software\\Bohemia Interactive\\cfgconvert", "path", "");

        public PboFS(PboFSTree fileTree, ArchiveManager archiveManager) : this(fileTree, archiveManager, "")
        {
            
        }

        public PboFS(PboFSTree fileTree, ArchiveManager archiveManager, string prefix)
        {
            this.archiveManager = archiveManager;
            this.fileTree = fileTree;
            this.prefix = prefix;
        }

        public static bool HasCfgConvert()
        {
            return System.IO.File.Exists(CFG_CONVERT_PATH + "\\CfgConvert.exe");
        }

        //#TODO only used by the PboFSDebinarizedConfig. Could move this to seperate file.
        public static System.IO.Stream DeRapConfig(System.IO.Stream input, long fileSize, byte[] buffer)
        {
            var tempFileName = System.IO.Path.GetTempFileName();
            var file = System.IO.File.Create(tempFileName);

            //CopyTo with set number of bytes
            var bytes = fileSize;
            int read;
            while (bytes > 0 &&
                   (read = input.Read(buffer, 0, (int) Math.Min(buffer.Length, bytes))) > 0)
            {
                file.Write(buffer, 0, read);
                bytes -= read;
            }

            file.Close();

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                CreateNoWindow = false,
                UseShellExecute = false,
                FileName = CFG_CONVERT_PATH + "\\CfgConvert.exe",
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = "-txt " + tempFileName
            };

            try
            {
                using (var exeProcess = Process.Start(startInfo))
                {
                    exeProcess?.WaitForExit();
                }
            }
            catch
            {
                return null;
            }


            var tempFileStream = new System.IO.FileStream(tempFileName, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.ReadWrite, 4096, System.IO.FileOptions.DeleteOnClose);
            tempFileStream.Seek(0, System.IO.SeekOrigin.Begin);

            return tempFileStream;
        }

        private IPboFsNode FindNode(string filename)
        {
            var prefixedFilename = PrefixedFilename(filename);

            return fileTree.NodeForPath(prefixedFilename);
        }

        private void DeleteNode(string filename)
        {
            fileTree.DeleteNode(PrefixedFilename(filename));
        }

        private IPboFsNode GetNodeFast(string filename, DokanFileInfo info)
        {
            if (info?.Context is IPboFsNode node)
                return node;
            return FindNode(filename);
        }

        public void Cleanup(string filename, DokanFileInfo info)
        {
            if (GetNodeFast(filename, info) is IPboFsFile file)
                file.Close();
            //#TODO Functions in DOKAN_OPERATIONS struct need to be thread-safe, because they are called in several threads
            //https://dokan-dev.github.io/dokany-doc/html/
            //Might be important for file handles we open/close. One might close in the same time another on is reading. But closing at Cleanup should be safe
        }

        public void CloseFile(string filename, DokanFileInfo info)
        {
            //If the file is immediately reopened after closing. CloseFile might not be called.
            //In that case it would be beneficial for performance to keep the handle open.
            //Cleanup is actually what corresponds to all handles to a file being closed and gone.
            //https://dokan-dev.github.io/dokany-doc/html/
        }


        private PboFsFolder CreateOrFindDirectoryRecursive(string fullPath)
        {
            var node = FindNode(fullPath);
            if (node != null) return node as PboFsFolder;

            var currentPath = "\\";
            var splitPath = fullPath.Remove(0,1).Split('\\');

            PboFsFolder currentFolder = FindNode("\\") as PboFsFolder;//get root node

            // Create folder for all sub paths
            foreach (var folderName in splitPath)
            {
                currentPath += folderName;

                PboFsFolder folder = null;
                var foundNode = FindNode(currentPath);
                if (foundNode == null)
                {
                    folder = new PboFsFolder(folderName, currentFolder);
                    fileTree.AddNode(folder);
                }
                else
                {
                    folder = (PboFsFolder)foundNode;
                }

                if (!currentFolder.Children.ContainsKey(folderName.ToLower()))
                {
                    currentFolder.Children[folderName.ToLower()] = currentFolder = folder;
                }
                else
                {
                    currentFolder = (PboFsFolder)currentFolder.Children[folderName.ToLower()];
                }

                currentPath += "\\";
            }
            return currentFolder;
        }

        public NtStatus CreateFile(string filename, FileAccess access, System.IO.FileShare share, System.IO.FileMode mode, System.IO.FileOptions options, System.IO.FileAttributes attributes, DokanFileInfo info)
        {
            var node = GetNodeFast(filename, info);

            if (node == null)
            {
                if (access == FileAccess.Delete) return DokanResult.Success;//already gone
                switch (mode)
                {
                    case FileMode.CreateNew:
                    case FileMode.Create:
                    case FileMode.OpenOrCreate:
                        if (filename.Length == 0)
                            return NtStatus.Success;
                        string Directory = filename;
                        if (!info.IsDirectory) //Directory doesn't have a filename that we want to cut off
                        {
                            Directory = filename.Substring(0, filename.LastIndexOf('\\'));
                            if (Directory.Length == 0)
                                Directory = "\\";
                        }
                        

                        var nodeDirectory = CreateOrFindDirectoryRecursive(Directory);

                        if (!(nodeDirectory is PboFsRealFolder) && nodeDirectory is PboFsFolder virtualFolder)
                        {
                            nodeDirectory = fileTree.MakeDirectoryWriteable(virtualFolder);
                        }

                        if (nodeDirectory is PboFsRealFolder folder)
                        {

                            if (info.IsDirectory)
                            {
                                info.Context = nodeDirectory;
                                //Nothing else to do as full path is already included in DirectoryPath
                            }
                            else
                            {

                                //Filename without folder path
                                var FileNameDirect = filename.Substring(filename.LastIndexOf('\\'));
                                var FileNameDirectNoLeadingSlash = filename.Substring(filename.LastIndexOf('\\') + 1);

                                FileStream newStream = null;
                                try
                                {
                                    newStream = System.IO.File.Create(folder.path + FileNameDirect);
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine(e);
                                    return DokanResult.AccessDenied;//#TODO correct result for exception type
                                }

                                var rlFile = new PboFsRealFile(new System.IO.FileInfo(folder.path + FileNameDirect), folder, newStream);

                                folder.Children[FileNameDirectNoLeadingSlash.ToLower()] = rlFile;
                                fileTree.AddNode(rlFile);
                                info.Context = rlFile;
                            }

                            return DokanResult.Success;
                        }

                        return DokanResult.DiskFull;
                    case FileMode.Open:
                    case FileMode.Truncate:
                    case FileMode.Append:
                        return DokanResult.FileNotFound;
                }
            }

            info.Context = node;
            if (node is PboFsFolder && !info.IsDirectory) info.IsDirectory = true; //Dokan documentation says we need to do that.

            if (mode == FileMode.CreateNew) return DokanResult.FileExists;

            if (access == FileAccess.Delete)
            {
                NtStatus deleteResult = DokanResult.NotImplemented;
                if (node is PboFsRealFile)
                    deleteResult = DeleteFile(filename, info);
                else if (node is PboFsRealFolder)
                    deleteResult = DeleteDirectory(filename, info);

                return deleteResult;
            }

            bool wantsWrite = (access & 
                               (FileAccess.WriteData | FileAccess.AppendData | FileAccess.Delete | FileAccess.GenericWrite)
                              ) != 0;

            bool wantsRead = (access &
                              (FileAccess.ReadData | FileAccess.GenericRead | FileAccess.Execute | FileAccess.GenericExecute)
                             ) != 0;

            if (wantsWrite && !(node is IPboFsRealObject))
                return DokanResult.AccessDenied;

            if (node is IPboFsFile file && (wantsRead || wantsWrite))
                return file.Open(wantsWrite, mode);

            return DokanResult.Success;
        }

        public NtStatus DeleteDirectory(string filename, DokanFileInfo info)
        {
            //This is called after Windows asked the user for confirmation.
            var gnf = GetNodeFast(filename, info);
            if (!(gnf is PboFsRealFolder folder)) return DokanResult.NotImplemented;

            try
            {
                foreach (var subfile in folder.Children.Keys.ToArray()) //Remove all Children from fileTree
                {
                    if (folder.Children[subfile] is PboFsRealFolder)
                        DeleteDirectory(filename + "\\" + subfile, null);
                    else
                    {
                        DeleteNode(filename + "\\" + subfile);
                        folder.Children.Remove(subfile);
                    }
                }

                System.IO.Directory.Delete(folder.path, true);
            }
            catch (AccessViolationException e)
            {
                return DokanResult.AccessDenied;
            }
            catch (IOException e)
            {
                return DokanResult.SharingViolation;
            }
            catch (Exception e)
            {
                return DokanResult.NotImplemented;
            }

            DeleteNode(filename); //Remove myself

            folder.parent?.Children?.Remove(folder.FileInformation.FileName.ToLower()); //Remove myself from parent

            return DokanResult.Success;
        }

        public NtStatus DeleteFile(string filename, DokanFileInfo info)
        {
            //This is called after Windows asked the user for confirmation.

            if (GetNodeFast(filename, info) is PboFsRealFile file)
            {
                try
                {
                    file.Close();
                    System.IO.File.Delete(file.GetRealPath());
                }
                catch (DirectoryNotFoundException e)
                {
                    //File is already gone. Just return success
                }

                DeleteNode(filename);
                file.parent?.Children?.Remove(file.FileInformation.FileName.ToLower());
                return DokanResult.Success;
            }

            return DokanResult.NotImplemented;
        }

        public NtStatus FlushFileBuffers(string filename, DokanFileInfo info)
        {
            if (GetNodeFast(filename, info) is PboFsRealFile file)
            {
                file.Flush();
                return DokanResult.Success;
            }

            return DokanResult.NotImplemented;
        }

        public NtStatus FindFiles(string filename, out IList<FileInformation> files, DokanFileInfo info)
        {
            files = this.fileTree.FilesForPath(PrefixedFilename(filename));
            return files != null ? DokanResult.Success : DokanResult.FileNotFound;
        }

        public NtStatus GetFileInformation(string filename, out FileInformation fileInfo, DokanFileInfo info)
        {
            var node = GetNodeFast(filename, info);
            if (node == null)
            {
                fileInfo = new FileInformation();
                return DokanResult.FileNotFound;
            }

            fileInfo = new FileInformation
            {
                FileName = filename, //In GetFileInformation this is only used to get the hash of the string which is returned as the INODE.
                //Everywhere else FileName has to be the actual filename but not here. https://github.com/dokan-dev/dokan-dotnet/issues/196
                Attributes = node.FileInformation.Attributes,
                CreationTime = node.FileInformation.CreationTime,
                LastAccessTime = node.FileInformation.LastAccessTime,
                LastWriteTime = node.FileInformation.LastWriteTime,
                Length = node.FileInformation.Length
            };
            return DokanResult.Success;
        }

        public NtStatus MoveFile(string filename, string newname, bool replace, DokanFileInfo info)
        {
            var sourceNode = GetNodeFast(filename, info);

            var sourceDirectory = filename.Substring(0, filename.LastIndexOf('\\'));
            if (sourceDirectory.Length == 0)
                sourceDirectory = "\\";

            var targetDirectory = newname.Substring(0, newname.LastIndexOf('\\'));
            if (targetDirectory.Length == 0)
                targetDirectory = "\\";

            var nodeSourceDirectory = FindNode(sourceDirectory) as PboFsFolder;
            var nodeTargetDirectory = FindNode(targetDirectory) as PboFsFolder;

            if (nodeSourceDirectory == null)
            {
                Console.WriteLine("DokanPBO::MoveFile failed because source directory doesn't exist: " + sourceDirectory);
                return DokanResult.FileNotFound;
            }

            if (nodeTargetDirectory == null)
            {
                Console.WriteLine("DokanPBO::MoveFile failed because target directory doesn't exist: " + targetDirectory);
                return DokanResult.FileNotFound;
            }

            var sourceFilenameDirect = filename.Substring(filename.LastIndexOf('\\') + 1);
            var targetFilenameDirect = newname.Substring(newname.LastIndexOf('\\') + 1);

            switch (sourceNode)
            {
                case null:
                    return DokanResult.FileNotFound;
                case PboFsRealFile file:
                    var targetNode = FindNode(newname);
                    if (targetNode != null && !replace)
                        return DokanResult.FileExists;

                    if (file.IsOpen()) return DokanResult.SharingViolation;

                    nodeTargetDirectory.Children.Add(targetFilenameDirect.ToLower(), nodeSourceDirectory.Children[sourceFilenameDirect.ToLower()]);
                    nodeSourceDirectory.Children.Remove(sourceFilenameDirect.ToLower());

                    fileTree.DeleteNode(file);

                    System.IO.File.Move(file.GetRealPath(), fileTree.writeableDirectory + newname);

                    file.file = new FileInfo(fileTree.writeableDirectory + newname);
                    file.FileInformation.FileName = targetFilenameDirect;
                    file.parent = nodeTargetDirectory;

                    fileTree.AddNode(file);

                    return DokanResult.Success;
                case PboFsRealFolder folder:
                    targetNode = FindNode(newname);
                    if (targetNode != null && !replace)
                        return DokanResult.FileExists;

                    nodeTargetDirectory.Children.Add(targetFilenameDirect.ToLower(), nodeSourceDirectory.Children[sourceFilenameDirect.ToLower()]);
                    nodeSourceDirectory.Children.Remove(sourceFilenameDirect.ToLower());


                    void doNodesRecursive(PboFsFolder folderIn, bool remove)
                    {
                        foreach (var entry in folderIn.Children)
                        {
                            if (remove)
                            {
                                fileTree.DeleteNode(entry.Value);
                                if (entry.Value is PboFsRealFile file)
                                {
                                    file.Close();
                                }
                            }
                                
                            else
                            {
                                fileTree.AddNode(entry.Value);
                                if (entry.Value is PboFsRealFile file)
                                {
                                    file.file = new FileInfo(file.file.FullName.Replace(filename,newname));
                                }

                            }
                                
                            if (entry.Value is PboFsFolder nextFolder)
                                doNodesRecursive(nextFolder, remove);
                        }
                    }



                    fileTree.DeleteNode(folder);
                    doNodesRecursive(folder, true);

                    System.IO.Directory.Move(folder.path, fileTree.writeableDirectory + newname);
                    folder.path = fileTree.writeableDirectory + newname;
                    folder.FileInformation.FileName = targetFilenameDirect;
                    folder.parent = nodeTargetDirectory;

                    fileTree.AddNode(folder);

                    doNodesRecursive(folder, false);

                    return DokanResult.Success;
                default:
                    return DokanResult.NotImplemented; //Source node is immovable
            }
        }

        public NtStatus ReadFile(string filename, byte[] buffer, out int readBytes, long offset, DokanFileInfo info)
        {
            if (GetNodeFast(filename, info) is IPboFsFile file)
            {
                var result = file.ReadFile(buffer, out readBytes, offset);
                if (result == DokanResult.FileNotFound)
                    DeleteNode(filename);

                return result;
            }
            
            readBytes = 0;
            return DokanResult.Error;
        }

        public NtStatus WriteFile(string filename, byte[] buffer, out int writtenBytes, long offset, DokanFileInfo info)
        {
            if (GetNodeFast(filename, info) is PboFsRealFile file)
            {
                var result = file.WriteFile(buffer, out writtenBytes, offset);
                if (result == DokanResult.FileNotFound)
                    DeleteNode(filename);

                return result;
            }

            writtenBytes = 0;
            return DokanResult.AccessDenied;
        }

        public NtStatus SetEndOfFile(string filename, long length, DokanFileInfo info)
        {
            if (GetNodeFast(filename, info) is PboFsRealFile file)
            {
                var result = file.SetEof(length);
                if (result == DokanResult.FileNotFound)
                    DeleteNode(filename);

                return result;
            }

            return DokanResult.Error;
        }

        public NtStatus SetAllocationSize(string filename, long length, DokanFileInfo info)
        {
            return SetEndOfFile(filename, length, info);
        }

        public NtStatus SetFileAttributes(string filename, System.IO.FileAttributes attr, DokanFileInfo info)
        {
            var node = GetNodeFast(filename, info);

            if (node is PboFsRealFile file)
            {
                System.IO.File.SetAttributes(file.GetRealPath(), attr);
                file.FileInformation.Attributes = attr;
                return DokanResult.Success;
            }

            if (node is PboFsRealFolder folder)
            {
                System.IO.File.SetAttributes(folder.path, attr | FileAttributes.Directory);
                folder.FileInformation.Attributes = attr | FileAttributes.Directory;
                return DokanResult.Success;
            }

            return DokanResult.NotImplemented; //Error message "Invalid Function"
        }

        public NtStatus SetFileTime(string filename, DateTime? ctime, DateTime? atime, DateTime? wtime, DokanFileInfo info)
        {
            var node = GetNodeFast(filename, info);

            if (node is PboFsRealFile file)
            {
                //#TODO we can't do this while file is open. Optimally we just want to queue tasks to be executed once the file is closed.
                if (file.IsOpenForWriting) return DokanResult.Success;
                if (ctime != null)
                {
                    System.IO.File.SetCreationTime(file.GetRealPath(), ctime.Value);
                    file.FileInformation.CreationTime = ctime.Value;
                }
                   
                if (atime != null)
                {
                    System.IO.File.SetLastAccessTime(file.GetRealPath(), atime.Value);
                    file.FileInformation.LastAccessTime = atime.Value;
                }
                    
                if (wtime != null)
                {
                    file.SetLastWriteTime(wtime.Value);
                }

                return DokanResult.Success;
            }

            if (node is PboFsRealFolder folder)
            {
                if (ctime != null)
                {
                    System.IO.Directory.SetCreationTime(folder.GetRealPath(), ctime.Value);
                    folder.FileInformation.CreationTime = ctime.Value;
                }

                if (atime != null)
                {
                    System.IO.Directory.SetLastAccessTime(folder.GetRealPath(), atime.Value);
                    folder.FileInformation.LastAccessTime = atime.Value;
                }

                if (wtime != null)
                {
                    System.IO.Directory.SetLastWriteTime(folder.GetRealPath(), wtime.Value);
                    folder.FileInformation.LastWriteTime = wtime.Value;
                }

                return DokanResult.Success;
            }


            return DokanResult.AccessDenied;
        }

        public NtStatus Mounted(DokanFileInfo info)
        {
            Console.WriteLine("DokanPbo mounted!");
            return DokanResult.Success;
        }

        public NtStatus Unmounted(DokanFileInfo info)
        {
            Console.WriteLine("DokanPbo unmounted?!?");
            return DokanResult.Success;
        }

        public NtStatus GetDiskFreeSpace(out long freeBytesAvailable, out long totalBytes, out long totalFreeBytes, DokanFileInfo info)
        {
            var drive = new DriveInfo(fileTree.writeableDirectory);

            freeBytesAvailable = drive.AvailableFreeSpace;
            totalBytes = this.archiveManager.TotalBytes;
            totalFreeBytes = drive.TotalFreeSpace;

            return DokanResult.Success;
        }

        public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features, out string fileSystemName, out uint maximumComponentLength, DokanFileInfo info)
        {
            volumeLabel = "PboFS";
            features = FileSystemFeatures.None;
            fileSystemName = "PboFS";
            maximumComponentLength = 256;
            return DokanResult.Success;
        }

        public NtStatus GetFileSecurity(string filename, out FileSystemSecurity security, AccessControlSections sections, DokanFileInfo info)
        {
            var node = GetNodeFast(filename, info);
            if (node is IPboFsRealObject realObject)
            {
                security = node is PboFsRealFile ? File.GetAccessControl(realObject.GetRealPath()) : (FileSystemSecurity) Directory.GetAccessControl(realObject.GetRealPath());
                return DokanResult.Success;
            }

            security = null;
            return DokanResult.AccessDenied;
        }

        public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections, DokanFileInfo info)
        {
            return DokanResult.Error;
        }


        public NtStatus LockFile(string filename, long offset, long length, DokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus UnlockFile(string filename, long offset, long length, DokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus EnumerateNamedStreams(string fileName, IntPtr enumContext, out string streamName, out long streamSize, DokanFileInfo info)
        {
            streamName = string.Empty;
            streamSize = 0;
            return DokanResult.NotImplemented;
        }

        public NtStatus FindStreams(string fileName, out IList<FileInformation> streams, DokanFileInfo info)
        {
            streams = null;
            return DokanResult.NotImplemented;
        }

        public NtStatus FindFilesWithPattern(string fileName, string searchPattern, out IList<FileInformation> files, DokanFileInfo info)
        {
            files = null;
            return DokanResult.NotImplemented;
        }

        private string PrefixedFilename(string filename)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return filename.ToLower();
            }

            if (filename == "\\")
            {
                return this.prefix.ToLower();
            }

            return (this.prefix + filename).ToLower();
        }
    }
}
