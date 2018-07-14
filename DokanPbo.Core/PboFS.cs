using DokanNet;
using Microsoft.Win32;
using SwiftPbo;
using System;
using System.Collections.Generic;
using System.Security.AccessControl;

namespace DokanPbo
{
    public class PboFS : IDokanOperations
    {
        private ArchiveManager archiveManager;
        private PboFSTree fileTree;
        private string prefix;

        public PboFS(PboFSTree fileTree, ArchiveManager archiveManager) : this(fileTree, archiveManager, "")
        {
            
        }

        public PboFS(PboFSTree fileTree, ArchiveManager archiveManager, string prefix)
        {
            this.archiveManager = archiveManager;
            this.fileTree = fileTree;
            this.prefix = prefix;
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
            var stream = this.archiveManager.ReadStream(PrefixedFilename(filename));
            FileEntry file = null;

            if (stream != null && this.archiveManager.FilePathToFileEntry.TryGetValue(PrefixedFilename(filename), out file))
            {
                stream.Position += offset;
                readBytes = stream.Read(buffer, 0, Math.Min(buffer.Length, (int) ((long) file.DataSize - offset)));
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
            fileSystemName = String.Empty;
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
            if (prefix == null || prefix.Length == 0)
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
