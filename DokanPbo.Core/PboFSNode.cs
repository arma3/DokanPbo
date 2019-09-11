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
        public PboFsFolder parent;

        public override int GetHashCode()
        {

            int hashCode = FileInformation.FileName?.ToLower().GetHashCode() ?? "".GetHashCode();
            var element = parent;
            while (element != null)
            {
                if (element.FileInformation.FileName != null && element.FileInformation.FileName != "")
                    hashCode = hashCode * 23 + element.FileInformation.FileName.ToLower().GetHashCode();
                element = element.parent;
            }

            return hashCode;
        }

        public override bool Equals(object obj)
        {

            if (obj is PboFsLookupDummy dummy)
                return dummy.Equals(this);

            if (obj is IPboFsNode node)
            {
                if (!FileInformation.FileName.Equals(node.FileInformation.FileName, StringComparison.CurrentCultureIgnoreCase)) return false;

                var lparent = parent;
                var rparent = node.parent;
                while (lparent != null && rparent != null)
                {
                    if ((lparent.FileInformation.FileName == null || rparent.FileInformation.FileName == null))
                        return rparent.FileInformation.FileName == lparent.FileInformation.FileName;

                    if (!lparent.FileInformation.FileName.Equals(rparent.FileInformation.FileName, StringComparison.CurrentCultureIgnoreCase)) return false;
                    lparent = lparent.parent;
                    rparent = rparent.parent;
                }

                return rparent == lparent; //one of them is null. If the other isn't also null then they didn't match.
            }

            return false;
        }
    }

    public class PboFsLookupDummy : IPboFsNode
    {
        private List<string> path = new List<string>();


        public PboFsLookupDummy(string inputPath)
        {
            var splitPath = inputPath.Split('\\');
            foreach (var it in splitPath)
                path.Add(it.ToLower());

            path.RemoveAll((x) => x == "");
            if (path.Count == 0) return;

            path.Reverse();
        }


        public override int GetHashCode()
        {
            if (path.Count == 0) return "".GetHashCode();

            bool first = true;
            int hashCode = 0;

            foreach (var it in path)
            {
                if (first)
                {
                    hashCode = it.GetHashCode();
                    first = false;
                } else
                    hashCode = hashCode * 23 + it.GetHashCode();
            }

            return hashCode;
        }

        public override bool Equals(object obj)
        {
            if (obj is IPboFsNode node)
            {

                if (parent == null && node.parent == null) return true; //Both are root node. Done here because root node doesn't have a path

                var rparent = node;
                foreach (var it in path)
                {
                    if (rparent == null) return false;
                    if (!it.Equals(rparent.FileInformation.FileName, StringComparison.CurrentCultureIgnoreCase)) return false;
                    rparent = rparent.parent;
                }

                return rparent?.parent == null; //If it is not null. We didn't arrive at root node which we should've
            }


            return false;
        }
    }

    public abstract class IPboFsFolder : IPboFsNode
    {

    }

    public abstract class IPboFsFile : IPboFsNode
    {
        public abstract NtStatus ReadFile(byte[] buffer, out int readBytes, long offset);
        
        //Can be used to prepare the Stream and keep it in cache
        public virtual NtStatus Open(bool write = false, FileMode mode = FileMode.Open)
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
        public Dictionary<string, IPboFsNode> Children;

        public PboFsFolder(string name, PboFsFolder inputParent) : base()
        {
            Children = new Dictionary<string, IPboFsNode>();
            parent = inputParent;
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

    public class PboFsFile : IPboFsFile
    {
        public FileEntry File;

        public PboFsFile(string name, FileEntry file, PboFsFolder inputParent) : base()
        {
            File = file;
            var fileTimestamp = new DateTime(1970, 1, 1, 0, 0, 0, 0).ToLocalTime().AddSeconds(file.TimeStamp);
            parent = inputParent;
            FileInformation = new DokanNet.FileInformation()
            {
                Attributes = System.IO.FileAttributes.Normal | FileAttributes.ReadOnly,
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

        public PboFsDebinarizedFile(string name, FileEntry file, PboFsFolder inputParent) : base(name, file, inputParent)
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
            FileInformation.Length = derapStream.Length;
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
        public System.IO.FileInfo file;
        private bool? wantsOpenWrite;
        private FileMode openMode = FileMode.Open;
        private System.IO.FileStream readStream = null;
        private System.IO.FileStream writeStream = null;
        public bool IsOpenForWriting => writeStream != null;

        //In case someone tries to set lastWriteTime while we have a Write stream open.
        private DateTime? lastWriteTimeTodo;

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

        //writeableStream is from newly created file
        public PboFsRealFile(System.IO.FileInfo inputFile, PboFsFolder inputParent, System.IO.FileStream writeableStream) : this(inputFile, inputParent)
        {
            writeStream = writeableStream;
        }


        //Might throw FileNotFoundException
        private System.IO.FileStream OpenStream(bool write)
        {
            return file.Open(openMode, write ? FileAccess.ReadWrite : FileAccess.Read, FileShare.ReadWrite);
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

        public override NtStatus Open(bool write, FileMode mode)
        {
            wantsOpenWrite = write;
            openMode = mode;
            return NtStatus.Success;
        }

        public override void Close()
        {
            if (!IsOpen()) return;

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
                Console.WriteLine("DokanPBO::ReadFile failed due to FileNotFoundException: " + e);
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
                Console.WriteLine("DokanPBO::WriteFile failed due to FileNotFoundException: " + e);
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
                file.Refresh();
                FileInformation.Length = length;
                return DokanResult.Success;
            }
            catch (FileNotFoundException e)
            {
                Console.WriteLine("DokanPBO::SetEof failed due to FileNotFoundException: " + e);
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

            System.IO.File.SetLastWriteTime(GetRealPath(), wtimeValue);
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
