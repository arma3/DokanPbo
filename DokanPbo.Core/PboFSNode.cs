using DokanNet;
using SwiftPbo;
using System;
using System.Collections.Generic;
using System.IO;

namespace DokanPbo
{
    public abstract class IPboFsNode
    {
        public FileInformation FileInformation;
    }

    public abstract class IPboFSFolder : IPboFsNode
    {

    }

    public abstract class IPboFSFile : IPboFsNode
    {
        public abstract NtStatus ReadFile(byte[] buffer, out int readBytes, long offset);
        
        //Can be used to prepare the Stream and keep it in cache
        public virtual NtStatus Open()
        {
            return NtStatus.Success;
        }

        public virtual void Close()
        {
        }

        public abstract long Filesize { get; }
    }

    public class PboFSFolder : IPboFSFolder
    {
        public Dictionary<string, IPboFsNode> Children;

        public PboFSFolder(string name) : base()
        {
            Children = new Dictionary<string, IPboFsNode>();
            FileInformation = new DokanNet.FileInformation()
            {
                Attributes = System.IO.FileAttributes.Directory,
                FileName = name,
                LastAccessTime = DateTime.Now,
                LastWriteTime = DateTime.Now,
                CreationTime = DateTime.Now,
            };
        }
    }

    public class PboFSFile : IPboFSFile
    {
        public FileEntry File;

        public PboFSFile(string name, FileEntry file) : base()
        {
            File = file;
            var fileTimestamp = new DateTime(1970, 1, 1, 0, 0, 0, 0).ToLocalTime().AddSeconds(file.TimeStamp);
            FileInformation = new DokanNet.FileInformation()
            {
                Attributes = System.IO.FileAttributes.Normal,
                FileName = name,
                Length = (long)file.DataSize,
                LastAccessTime = DateTime.Now,
                LastWriteTime = fileTimestamp,
                CreationTime = fileTimestamp,
            };
        }

        public override NtStatus ReadFile(byte[] buffer, out int readBytes, long offset)
        {
            if (offset > (long) File.DataSize)
            {
                readBytes = 0;
                return NtStatus.EndOfFile;
            }

            var stream = GetFileStream();
            stream.Position += offset;

            readBytes = stream.Read(buffer, 0, Math.Min(buffer.Length, (int) (Filesize - offset)));
            return DokanResult.Success;
        }

        public System.IO.Stream GetFileStream()
        {
            return File.Extract(); //#TODO cache with Open/Close functions
        }

        public override long Filesize => (long) File.DataSize;
    }

    public class PboFSDebinarizedFile : PboFSFile
    {
        public System.IO.Stream debinarizedStream = null;

        public PboFSDebinarizedFile(string name, FileEntry file) : base(name, file)
        {
        }

        public override NtStatus ReadFile(byte[] buffer, out int readBytes, long offset)
        {
            var stream = GetDebinarizedStream(buffer);

            //#TODO find a better error handling
            if (stream == null) //Return binarized file as fallback. 
                return base.ReadFile(buffer, out readBytes, offset);

            if (offset > stream.Length)
            {
                readBytes = 0;
                return NtStatus.EndOfFile;
            }

            stream.Position = offset;

            readBytes = stream.Read(buffer, 0, Math.Min(buffer.Length, (int)(Filesize - offset)));
            return DokanResult.Success;
        }

        public System.IO.Stream GetDebinarizedStream(byte[] buffer)
        {
            if (debinarizedStream != null)
                return debinarizedStream;

            var derapStream = PboFS.DeRapConfig(base.GetFileStream(), base.Filesize, buffer);
            debinarizedStream = derapStream;
            return derapStream;
        }

        public override long Filesize => debinarizedStream?.Length ?? base.Filesize;
    }

    public class PboFSRealFolder : PboFSFolder
    {
        public PboFSFolder parent;
        public string path = null;

        public PboFSRealFolder(string name, string inputPath, PboFSFolder inputParent) : base(name)
        {
            path = inputPath;
            parent = inputParent;
        }
    }

    public class PboFSRealFile : IPboFSFile
    {
        public PboFSFolder parent;
        public System.IO.FileInfo file;

        public PboFSRealFile(System.IO.FileInfo inputFile, PboFSFolder inputParent) : base()
        {
            file = inputFile;
            parent = inputParent;
            
            FileInformation = new DokanNet.FileInformation()
            {
                Attributes = file.Attributes,
                FileName = file.Name,
                Length = file.Length,
                LastAccessTime = file.LastAccessTime,
                LastWriteTime = file.LastWriteTime,
                CreationTime = file.CreationTime,
            };
        }

        public override NtStatus ReadFile(byte[] buffer, out int readBytes, long offset)
        {
            if (offset > file.Length)
            {
                readBytes = 0;
                return NtStatus.EndOfFile;
            }

            FileStream stream = null;
            try
            {
                stream = file.OpenRead();
            }
            catch (FileNotFoundException e)
            {
                readBytes = 0;
                return DokanResult.FileNotFound;
            }
            
            stream.Position = offset;
            
            //#TODO check if Read offset parameter can be used
            readBytes = stream.Read(buffer, 0, Math.Min(buffer.Length, (int)(Filesize - offset)));
            stream.Close(); //#TODO cache via Open/Close methods
            return DokanResult.Success;
        }

        public NtStatus WriteFile(byte[] buffer, out int writtenBytes, long offset)
        {
            if (offset > file.Length)
            {
                writtenBytes = 0;
                return NtStatus.EndOfFile;
            }

            FileStream stream = null;
            try
            {
                stream = file.OpenWrite();
            }
            catch (FileNotFoundException e)
            {
                writtenBytes = 0;
                return DokanResult.FileNotFound;
            } //#TODO access denied

            stream.Position = offset;

            writtenBytes = buffer.Length;
            stream.Write(buffer, 0, buffer.Length);
            stream.Flush();


            file.Refresh();
            FileInformation.Length = file.Length;
            stream.Close(); //#TODO cache via Open/Close methods
            return DokanResult.Success;
        }


        public override long Filesize => file.Length;
    }


}
