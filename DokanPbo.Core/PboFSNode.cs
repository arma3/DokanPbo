using DokanNet;
using SwiftPbo;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using FileAccess = System.IO.FileAccess;

namespace DokanPbo
{
    public abstract class IPboFsNode
    {
        public FileInformation FileInformation;
    }

    public abstract class IPboFsFolder : IPboFsNode
    {

    }

    public abstract class IPboFsFile : IPboFsNode
    {
        public abstract NtStatus ReadFile(byte[] buffer, out int readBytes, long offset);
        
        //Can be used to prepare the Stream and keep it in cache
        public virtual NtStatus Open(bool write = false)
        {
            return NtStatus.Success;
        }

        public virtual void Close()
        {
        }

        public abstract long Filesize { get; }
    }

    public interface IPboFsRealObject
    {
        string GetRealPath();
    }


    public class PboFsFolder : IPboFsFolder
    {
        public PboFsFolder parent;
        public Dictionary<string, IPboFsNode> Children;

        public PboFsFolder(string name, PboFsFolder inputParent) : base()
        {
            Children = new Dictionary<string, IPboFsNode>();
            parent = inputParent;
            FileInformation = new DokanNet.FileInformation()
            {
                Attributes = System.IO.FileAttributes.Directory,// | FileAttributes.ReadOnly | FileAttributes.Temporary,
                FileName = name,
                LastAccessTime = DateTime.Now,
                LastWriteTime = DateTime.Now,
                CreationTime = DateTime.Now,
            };
        }
    }

    public class PboFsFile : IPboFsFile
    {
        public FileEntry File;

        public PboFsFile(string name, FileEntry file) : base()
        {
            File = file;
            var fileTimestamp = new DateTime(1970, 1, 1, 0, 0, 0, 0).ToLocalTime().AddSeconds(file.TimeStamp);
            FileInformation = new DokanNet.FileInformation()
            {
                Attributes = System.IO.FileAttributes.Normal | FileAttributes.ReadOnly | FileAttributes.Temporary,
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

    public class PboFsDebinarizedFile : PboFsFile
    {
        public System.IO.Stream debinarizedStream = null;

        public PboFsDebinarizedFile(string name, FileEntry file) : base(name, file)
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

    public class PboFsRealFolder : PboFsFolder, IPboFsRealObject
    {
        public string path = null;

        public PboFsRealFolder(string name, string inputPath, PboFsFolder inputParent) : base(name, inputParent)
        {
            path = inputPath;

            var dirInfo = new DirectoryInfo(path);
            FileInformation.Attributes = dirInfo.Attributes;
            FileInformation.CreationTime = dirInfo.CreationTime;
            FileInformation.LastWriteTime = dirInfo.LastWriteTime;
            FileInformation.LastWriteTime = dirInfo.LastWriteTime;

        }

        public string GetRealPath()
        {
            return path;
        }
    }

    public class PboFsRealFile : IPboFsFile, IPboFsRealObject
    {
        public PboFsFolder parent;
        public System.IO.FileInfo file;
        private bool? wantsOpenWrite;
        private System.IO.FileStream readStream = null;
        private System.IO.FileStream writeStream = null;

        //In case someone tries to set lastWriteTime while we have a Write stream open.
        private DateTime? lastWriteTimeTodo;

        //Might throw FileNotFoundException
        private System.IO.FileStream OpenStream(bool write)
        {
            //Console.WriteLine("OpenActual " + write + " " + file.FullName);
            return file.Open(FileMode.Open, write ? FileAccess.ReadWrite : FileAccess.Read, FileShare.ReadWrite);
        }

        //Might throw FileNotFoundException
        //This doesn't cache the Stream
        private (System.IO.FileStream stream, bool fromCache) GetStream(bool write)
        {
            if (writeStream != null)
                return (writeStream, true); //writeStream can read and write

            if (!write && readStream != null)
                return (readStream, true);

            if (wantsOpenWrite != null) //Open stream and keep open till Close()
            {
                if (wantsOpenWrite.Value)
                {
                    writeStream = OpenStream(true);
                    return (writeStream, true);
                }
                else
                {
                    readStream = OpenStream(false);
                    return (readStream, true);
                }

            }

            return write ? (OpenStream(true), false) : (OpenStream(false), false);
        }

        public override NtStatus Open(bool write)
        {
            wantsOpenWrite = write;
            //Console.WriteLine("OpenSet " + write + " " + file.FullName);
            return NtStatus.Success;
        }

        public override void Close()
        {
            if (!IsOpen()) return;

            //Console.WriteLine("Close " + file.FullName);

            writeStream?.Flush();
            writeStream?.Close();
            writeStream?.Dispose(); //Might want to only dispose unmanaged resources aka the file handle https://msdn.microsoft.com/en-us/library/fy2eke69(v=vs.110).aspx
            writeStream = null;

            readStream?.Flush();
            readStream?.Close();
            readStream?.Dispose();
            readStream = null;
            if (lastWriteTimeTodo != null)
                SetLastWriteTime(lastWriteTimeTodo.Value);
            lastWriteTimeTodo = null;
        }

        public PboFsRealFile(System.IO.FileInfo inputFile, PboFsFolder inputParent) : base()
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

        public PboFsRealFile(System.IO.FileInfo inputFile, PboFsFolder inputParent, System.IO.FileStream writeableStream) : this(inputFile, inputParent)
        {
            //Console.WriteLine("OpenWStream " + file.FullName);
            writeStream = writeableStream;
        }

        public override NtStatus ReadFile(byte[] buffer, out int readBytes, long offset)
        {
            if (offset > file.Length)
            {
                readBytes = 0;
                return NtStatus.EndOfFile;
            }

            try
            {
                var (stream, streamFromCache) = GetStream(false);

                stream.Position = offset;
                //#TODO check if Read offset parameter can be used
                readBytes = stream.Read(buffer, 0, Math.Min(buffer.Length, (int)(Filesize - offset)));

                if (!streamFromCache)
                    stream.Close();

                return DokanResult.Success;
            }
            catch (FileNotFoundException e)
            {
                readBytes = 0;
                return DokanResult.FileNotFound;
            }
        }

        public NtStatus WriteFile(byte[] buffer, out int writtenBytes, long offset)
        {
            try
            {
                var (stream, streamFromCache) = GetStream(true);

                stream.Position = offset;

                writtenBytes = buffer.Length;
                stream.Write(buffer, 0, buffer.Length);
                stream.Flush();

                file.Refresh();
                FileInformation.Length = file.Length;

                if (!streamFromCache)
                    stream.Close();

                return DokanResult.Success;
            }
            catch (FileNotFoundException e)
            {
                writtenBytes = 0;
                return DokanResult.FileNotFound;
            } //#TODO access denied exception
        }

        public NtStatus SetEof(long length)
        {
            try
            {
                var (stream, streamFromCache) = GetStream(true);

                stream.SetLength(length);
                stream.Flush();

                if (!streamFromCache)
                    stream.Close(); //#TODO cache
                return DokanResult.Success;
            }
            catch (FileNotFoundException e)
            {
                return DokanResult.FileNotFound;
            } //#TODO access denied
        }


        public override long Filesize => file.Length;

        public void SetLastWriteTime(DateTime wtimeValue)
        {
            if (writeStream != null)
            {
                FileInformation.LastWriteTime = wtimeValue;
                lastWriteTimeTodo = wtimeValue;
                return;
            }

            System.IO.File.SetLastWriteTime(file.FullName, wtimeValue);
        }

        public void Flush()
        {
            writeStream?.Flush();
            readStream?.Flush();
        }

        public string GetRealPath()
        {
            return file.FullName;
        }

        public bool IsOpen()
        {
            return writeStream != null || readStream != null;
        }
    }


}
