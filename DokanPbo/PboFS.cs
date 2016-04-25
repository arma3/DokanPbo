using DokanNet;
using Microsoft.Win32;
using SwiftPbo;
using System;
using System.Collections.Generic;
using System.Security.AccessControl;

namespace DokanPbo
{
    internal class PboFS : IDokanOperations
    {
        private PboArchive Archive;

        public PboFS(string path)
        {
            Archive = new PboArchive(path);
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
            files = new List<FileInformation>();
            if (filename == "\\")
            {
                foreach (FileEntry file in Archive.Files)
                {
                    FileInformation fileInfo = new FileInformation();
                    fileInfo.FileName = file.FileName;
                    fileInfo.Length = (long) file.DataSize;
                    fileInfo.Attributes = System.IO.FileAttributes.Normal;
                    fileInfo.LastAccessTime = DateTime.Now;
                    fileInfo.LastWriteTime = DateTime.Now;
                    fileInfo.CreationTime = DateTime.Now;
                    files.Add(fileInfo);
                }
                return DokanResult.Success;
            }

            return DokanResult.Error;
        }

        public NtStatus GetFileInformation(string filename, out FileInformation fileInfo, DokanFileInfo info)
        {
            fileInfo = new FileInformation();
            fileInfo.FileName = filename;

            if (filename == "\\")
            {
                fileInfo.Attributes = System.IO.FileAttributes.Directory;
                fileInfo.LastAccessTime = DateTime.Now;
                fileInfo.LastWriteTime = DateTime.Now;
                fileInfo.CreationTime = DateTime.Now;

                return DokanResult.Success;
            }

            FileEntry file = null;
            if (file == null)
                return DokanResult.Error;

            fileInfo.Length = (long) file.DataSize;
            fileInfo.Attributes = System.IO.FileAttributes.Directory;
            fileInfo.LastAccessTime = DateTime.Now;
            fileInfo.LastWriteTime = DateTime.Now;
            fileInfo.CreationTime = DateTime.Now;

            return DokanResult.Success;
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
            totalBytes = 0;
            totalFreeBytes = 0;

            foreach (FileEntry file in Archive.Files)
            {
                totalBytes += (long) file.DataSize;
            }

            return DokanResult.Success;
        }

        public NtStatus WriteFile(string filename, byte[] buffer, out int writtenBytes, long offset, DokanFileInfo info)
        {
            writtenBytes = 0;
            return DokanResult.Error;
        }

        public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features, out string fileSystemName, DokanFileInfo info)
        {
            volumeLabel = "PboFS";
            features = FileSystemFeatures.None;
            fileSystemName = String.Empty;
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
    }
}
