using DokanNet;
using SwiftPbo;
using System;
using System.Collections.Generic;

namespace DokanPbo
{
    abstract class PboFSNode
    {
        public FileInformation FileInformation;
    }

    class PboFSFolder : PboFSNode
    {
        public Dictionary<string, PboFSNode> Children;

        public PboFSFolder(string name) : base()
        {
            Children = new Dictionary<string, PboFSNode>();
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

    class PboFSFile : PboFSNode
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
    }
}
