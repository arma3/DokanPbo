using DokanNet;
using Microsoft.Win32;
using SwiftPbo;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.AccessControl;

namespace DokanPbo
{
    public class PboFS : IDokanOperations
    {
        private ArchiveManager archiveManager;
        private PboFSTree fileTree;
        private string prefix;

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

        private System.IO.Stream DeRapConfig(System.IO.Stream input, ulong fileSize, byte[] buffer)
        {
            var tempFileName = System.IO.Path.GetTempFileName();
            var file = System.IO.File.Create(tempFileName);

            //CopyTo with set number of bytes
            var bytes = (int)fileSize;
            int read;
            while (bytes > 0 &&
                   (read = input.Read(buffer, 0, Math.Min(buffer.Length, bytes))) > 0)
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

        public void Cleanup(string filename, DokanFileInfo info)
        {
        }

        public void CloseFile(string filename, DokanFileInfo info)
        {
        }

        public NtStatus CreateFile(string filename, FileAccess access, System.IO.FileShare share, System.IO.FileMode mode, System.IO.FileOptions options, System.IO.FileAttributes attributes, DokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus DeleteDirectory(string filename, DokanFileInfo info)
        {
            return DokanResult.Error;
        }

        public NtStatus DeleteFile(string filename, DokanFileInfo info)
        {
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
            try
            {
                fileInfo = this.fileTree.FileInfoForPath(PrefixedFilename(filename));
                return DokanResult.Success;
            } catch (Exception)
            {
                fileInfo = new FileInformation();
                return DokanResult.Error;
            }
        }

        public NtStatus LockFile(string filename, long offset, long length, DokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus MoveFile(string filename, string newname, bool replace, DokanFileInfo info)
        {
            return DokanResult.Error;
        }

        public NtStatus ReadFile(string filename, byte[] buffer, out int readBytes, long offset, DokanFileInfo info)
        {
            System.IO.Stream stream = null;
            ulong fileSize = 0;
            PboFSNode node = fileTree.NodeForPath(PrefixedFilename(filename));

            if (node is PboFSFile pboFile)
            {
                stream = pboFile.File.Extract();
                fileSize = pboFile.File.DataSize;
                stream.Position += offset;
            }

            if (node is PboFSDummyFile dummyFile)
            {
                if (dummyFile.stream != null)
                {
                    stream = dummyFile.stream;
                    fileSize = (ulong)dummyFile.stream.Length;
                    stream.Position = offset;
                }
                else
                {
                    var derapStream = DeRapConfig(stream, fileSize, buffer);
                    if (derapStream != null) //DeRap failed. Just return binary stream from pboFile
                    {
                        dummyFile.stream = derapStream;
                        stream = derapStream;
                        fileSize = (ulong)derapStream.Length;
                        dummyFile.FileInformation.Length = derapStream.Length;
                    }
                }
            }

            if (stream != null)
            {
                readBytes = stream.Read(buffer, 0, Math.Min(buffer.Length, (int) ((long)fileSize - offset)));
                return DokanResult.Success;
            }

            readBytes = 0;
            return DokanResult.Error;
        }

        public NtStatus SetEndOfFile(string filename, long length, DokanFileInfo info)
        {
            return DokanResult.Error;
        }

        public NtStatus SetAllocationSize(string filename, long length, DokanFileInfo info)
        {
            return DokanResult.Error;
        }

        public NtStatus SetFileAttributes(string filename, System.IO.FileAttributes attr, DokanFileInfo info)
        {
            return DokanResult.Error;
        }

        public NtStatus SetFileTime(string filename, DateTime? ctime, DateTime? atime, DateTime? mtime, DokanFileInfo info)
        {
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
            freeBytesAvailable = 0;
            totalBytes = this.archiveManager.TotalBytes;
            totalFreeBytes = 0;

            return DokanResult.Success;
        }

        public NtStatus WriteFile(string filename, byte[] buffer, out int writtenBytes, long offset, DokanFileInfo info)
        {
            writtenBytes = 0;
            return DokanResult.Error;
        }

        public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features, out string fileSystemName, out uint maximumComponentLength, DokanFileInfo info)
        {
            volumeLabel = "PboFS";
            features = FileSystemFeatures.ReadOnlyVolume;
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
