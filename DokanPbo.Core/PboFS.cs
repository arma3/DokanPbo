using DokanNet;
using Microsoft.Win32;
using SwiftPbo;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using FileAccess = DokanNet.FileAccess;

namespace DokanPbo
{
    public class PboFS : IDokanOperations
    {
        private ArchiveManager archiveManager;
        private PboFSTree fileTree;
        private string prefix;
        private Dictionary<string, IPboFsNode> activeHandles = new Dictionary<string, IPboFsNode>();


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

        private (IPboFsNode node, bool fromOpenFiles) FindNode(string filename)
        {
            var prefixedFilename = PrefixedFilename(filename);

            return activeHandles.TryGetValue(prefixedFilename, out var node) ? (node, true) : (fileTree.NodeForPath(prefixedFilename), false);
        }

        public void Cleanup(string filename, DokanFileInfo info)
        {
        }

        public void CloseFile(string filename, DokanFileInfo info)
        {
            var (node, fromOpenFiles) = FindNode(filename);

            if (node is IPboFSFile file)
                file.Close();

            if (fromOpenFiles)
                activeHandles.Remove(filename);
        }

        public NtStatus CreateFile(string filename, FileAccess access, System.IO.FileShare share, System.IO.FileMode mode, System.IO.FileOptions options, System.IO.FileAttributes attributes, DokanFileInfo info)
        {
            var (node, fromOpenFiles) = FindNode(filename);


            if (node == null)
            {
                switch (mode)
                {
                    case FileMode.CreateNew:
                    case FileMode.Create:
                    case FileMode.OpenOrCreate:
                        if (filename.Length == 0)
                            return NtStatus.Success;
                        var Directory = filename.Substring(0, filename.LastIndexOf('\\'));
                        if (Directory.Length == 0)
                            Directory = "\\";

                        var nodeDirectory = FindNode(Directory).node;

                        //Filename without folder path
                        var FileNameDirect = filename.Substring(filename.LastIndexOf('\\')).ToLower();

                        //#TODO if PboFSFolder create directory recursively if needed

                        if (nodeDirectory is PboFSRealFolder folder)
                        {

                            if (info.IsDirectory)
                            {
                                System.IO.Directory.CreateDirectory(folder.path + filename);//#TODO create directory recursively if needed

                                var rlFolder = new PboFSRealFolder(filename.Substring(filename.LastIndexOf('\\')+1), folder.path + filename, folder);

                                folder.Children[rlFolder.FileInformation.FileName.ToLower()] = rlFolder;
                                fileTree.AddNode(filename.ToLower(), rlFolder);
                            }
                            else
                            {
                                FileStream newStream = null;
                                try
                                {
                                    newStream = System.IO.File.Create(folder.path + FileNameDirect);
                                }
                                catch (Exception e)
                                {
                                    return DokanResult.AccessDenied;//#TODO correct result for exception type
                                }

                                //#TODO plug newStream into pbofile and add to activeHandles
                                var rlFile = new PboFSRealFile(new System.IO.FileInfo(folder.path + "\\" + FileNameDirect), folder);

                                folder.Children[FileNameDirect] = rlFile;
                                fileTree.AddNode(filename.ToLower(), rlFile);
                                newStream.Close();
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

            if (mode == FileMode.CreateNew) return DokanResult.FileExists;


            if (access == FileAccess.Delete)
            {
                NtStatus deleteResult = DokanResult.Error;
                if (node is PboFSRealFile)
                    deleteResult = DeleteFile(filename, info);
                else if (node is PboFSRealFolder)
                    deleteResult = DeleteDirectory(filename, info);

                if (deleteResult != DokanResult.Success)
                    return DokanResult.AccessDenied;
            }

            bool wantsWrite = (access & 
                               (FileAccess.WriteData | FileAccess.AppendData | FileAccess.Delete | FileAccess.GenericWrite)
                              ) != 0; 

            //#TODO check if can write
            if (wantsWrite && !(node is PboFSRealFolder) && !(node is PboFSRealFile))
                return DokanResult.AccessDenied;


            if (!fromOpenFiles)
                activeHandles[filename] = node;

            if (node is IPboFSFile file)
                return file.Open();

            return DokanResult.Success;
        }

        public NtStatus DeleteDirectory(string filename, DokanFileInfo info)
        {
            //This is called after Windows asked the user for confirmation.

            var node = FindNode(filename).node;

            if (node is PboFSRealFolder folder)
            {
                try
                {
                    System.IO.Directory.Delete(folder.path, true);
                }
                catch (Exception e)
                {
                    return DokanResult.Error;
                }
                
                fileTree.DeleteNode(PrefixedFilename(filename));

                foreach (var subfile in folder.Children.Keys) //Remove all Children from fileTree
                    fileTree.DeleteNode(PrefixedFilename(filename + "\\" + subfile));

                folder.parent?.Children?.Remove(node.FileInformation.FileName.ToLower());

                return DokanResult.Success;
            }

            return DokanResult.Error;
        }

        public NtStatus DeleteFile(string filename, DokanFileInfo info)
        {
            //This is called after Windows asked the user for confirmation.

            var node = FindNode(filename).node;

            if (node is PboFSRealFile file)
            {
                try
                {
                    System.IO.File.Delete(file.file.FullName);
                }
                catch (DirectoryNotFoundException e)
                {
                    //File is already gone. Just return success
                }

                fileTree.DeleteNode(PrefixedFilename(filename));
                file.parent?.Children?.Remove(node.FileInformation.FileName.ToLower());
                return DokanResult.Success;
            }

            return DokanResult.Error;
        }

        public NtStatus FlushFileBuffers(string filename, DokanFileInfo info)
        {
            return DokanResult.Error;
        }

        public NtStatus FindFiles(string filename, out IList<FileInformation> files, DokanFileInfo info)
        {
            try
            {
                files = this.fileTree.FilesForPath(PrefixedFilename(filename));
                return DokanResult.Success;
            }
            catch (Exception)
            {
                files = null;
                return DokanResult.Error;
            }
        }

        public NtStatus GetFileInformation(string filename, out FileInformation fileInfo, DokanFileInfo info)
        {
            var node = FindNode(filename);
            if (node.node == null)
            {
                fileInfo = new FileInformation();
                return DokanResult.FileNotFound;
            }

            fileInfo = node.node.FileInformation;
            return DokanResult.Success;
        }

        public NtStatus LockFile(string filename, long offset, long length, DokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus MoveFile(string filename, string newname, bool replace, DokanFileInfo info)
        {
            //#TODO implement. This is also for renaming files/folders Remember the need to move them in the writeDirectory too.

            return DokanResult.Error;
        }

        public NtStatus ReadFile(string filename, byte[] buffer, out int readBytes, long offset, DokanFileInfo info)
        {
            if (FindNode(filename).node is IPboFSFile file)
            {
                var result = file.ReadFile(buffer, out readBytes, offset);
                if (result == DokanResult.FileNotFound)
                    fileTree.DeleteNode(filename);

                return result;
            }
            
            readBytes = 0;
            return DokanResult.Error;
        }


        public NtStatus WriteFile(string filename, byte[] buffer, out int writtenBytes, long offset, DokanFileInfo info)
        {

            if (FindNode(filename).node is PboFSRealFile file)
            {
                var result = file.WriteFile(buffer, out writtenBytes, offset);
                if (result == DokanResult.FileNotFound)
                    fileTree.DeleteNode(filename);

                return result;
            }

            writtenBytes = 0;
            return DokanResult.AccessDenied;
        }


        public NtStatus SetEndOfFile(string filename, long length, DokanFileInfo info)
        {
            7z unpack error Can not set length for output file 
                Fix dis
            return DokanResult.Error;
        }

        public NtStatus SetAllocationSize(string filename, long length, DokanFileInfo info)
        {
            return DokanResult.Error;
        }

        public NtStatus SetFileAttributes(string filename, System.IO.FileAttributes attr, DokanFileInfo info)
        {
            var node = FindNode(filename).node;

            if (node is PboFSRealFile file)
            {
                System.IO.File.SetAttributes(file.file.FullName, attr);
                file.FileInformation.Attributes = attr;
                return DokanResult.Success;
            }

            return DokanResult.Error;
        }

        public NtStatus SetFileTime(string filename, DateTime? ctime, DateTime? atime, DateTime? wtime, DokanFileInfo info)
        {
            var node = FindNode(filename).node;

            if (node is PboFSRealFile file)
            {
                if (ctime != null)
                {
                    System.IO.File.SetCreationTime(file.file.FullName, ctime.Value);
                    file.FileInformation.CreationTime = ctime.Value;
                }
                   
                if (atime != null)
                {
                    System.IO.File.SetLastAccessTime(file.file.FullName, atime.Value);
                    file.FileInformation.LastAccessTime = atime.Value;
                }
                    
                if (wtime != null)
                {
                    System.IO.File.SetLastWriteTime(file.file.FullName, wtime.Value);
                    file.FileInformation.LastWriteTime = wtime.Value;
                }

                return DokanResult.Success;
            }

            if (node is PboFSRealFolder folder)
            {
                if (ctime != null)
                {
                    System.IO.Directory.SetCreationTime(folder.path, ctime.Value);
                    folder.FileInformation.CreationTime = ctime.Value;
                }

                if (atime != null)
                {
                    System.IO.Directory.SetLastAccessTime(folder.path, atime.Value);
                    folder.FileInformation.LastAccessTime = atime.Value;
                }

                if (wtime != null)
                {
                    System.IO.Directory.SetLastWriteTime(folder.path, wtime.Value);
                    folder.FileInformation.LastWriteTime = wtime.Value;
                }

                return DokanResult.Success;
            }


            return DokanResult.Error;
        }

        public NtStatus UnlockFile(string filename, long offset, long length, DokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus Mounted(DokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus Unmounted(DokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus GetDiskFreeSpace(out long freeBytesAvailable, out long totalBytes, out long totalFreeBytes, DokanFileInfo info)
        {
            System.IO.DriveInfo drive = new System.IO.DriveInfo("X:\\pbos");//#TODO use global writefiles folder

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

        public NtStatus GetFileSecurity(string fileName, out FileSystemSecurity security, AccessControlSections sections, DokanFileInfo info)
        {
            security = null;
            return DokanResult.Error;
        }

        public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections, DokanFileInfo info)
        {
            return DokanResult.Error;
        }

        public NtStatus EnumerateNamedStreams(string fileName, IntPtr enumContext, out string streamName, out long streamSize, DokanFileInfo info)
        {
            streamName = String.Empty;
            streamSize = 0;
            return DokanResult.NotImplemented;
        }

        public NtStatus FindStreams(string fileName, out IList<FileInformation> streams, DokanFileInfo info)
        {
            streams = new FileInformation[0];
            return DokanResult.NotImplemented;
        }

        public NtStatus FindFilesWithPattern(string fileName, string searchPattern, out IList<FileInformation> files, DokanFileInfo info)
        {
            files = new FileInformation[0];
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
