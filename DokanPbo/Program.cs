using CommandLine;
using CommandLine.Text;
using DokanNet;
using System;
using System.Collections.Generic;
using System.IO;
using DokanNet.Logging;

namespace DokanPbo
{
    class Options
    {
        [OptionArray('f', "folders", Required = true,
          HelpText = "Directories with PBO files to mount.")]
        public string[] PboDirectories { get; set; }

        [Option('o', "output", Required = true,
          HelpText = "Drive or directory where to mount.")]
        public string MountDirectory { get; set; }

        [Option("prefix",
          HelpText = "Prefix used to filter PBO paths.")]
        public string Prefix { get; set; }

        [Option('u', "unmount", Required = false,
          HelpText = "Drive or directory to unmount.")]
        public string UnmountDirectory { get; set; }

        [Option('w', "writedir", Required = false,
            HelpText = "Writeable directory to store files written to the mount.")]
        public string WriteableDirectory { get; set; }


        [HelpOption]
        public string GetUsage()
        {
            return HelpText.AutoBuild(this,
              (HelpText current) => HelpText.DefaultParsingErrorsHandler(this, current));
        }
    }

    class UnmountOptions
    {
        [Option('u', "unmount", Required = true,
          HelpText = "Drive or directory to unmount.")]
        public string UnmountDirectory { get; set; }
    }

    internal class Program
    {

#if DEBUG
        //static readonly DokanOptions MOUNT_OPTIONS = DokanOptions.DebugMode | DokanOptions.StderrOutput | DokanOptions.FixedDrive;
        static readonly DokanOptions MOUNT_OPTIONS = DokanOptions.DebugMode | DokanOptions.FixedDrive;
#else
        static readonly DokanOptions MOUNT_OPTIONS = DokanOptions.FixedDrive;
#endif

        private static void Main(string[] args)
        {
           

            var unmountOptions = new UnmountOptions();
            if (CommandLine.Parser.Default.ParseArguments(args, unmountOptions))
            {
                Dokan.RemoveMountPoint(unmountOptions.UnmountDirectory);
                return;
            }

            var options = new Options();
            if (CommandLine.Parser.Default.ParseArguments(args, options))
            {
                try
                {
                    Console.WriteLine("DokanPbo booting...");
                    bool writeAbleIsTemporary = options.WriteableDirectory == null;

                    if (writeAbleIsTemporary)
                    {
                        options.WriteableDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                        //#TODO can throw exception and die if it creates a existing folder by accident
                        Directory.CreateDirectory(options.WriteableDirectory);
                    }

                    if (!Directory.Exists(options.WriteableDirectory))
                    {
                        Console.WriteLine("FATAL Writeable Directory doesn't exist: " + options.WriteableDirectory);
                        Console.ReadKey();
                    }

                    ArchiveManager archiveManager = new ArchiveManager(options.PboDirectories);
                    PboFSTree fileTree = new PboFSTree(archiveManager, options.WriteableDirectory);
                    PboFS pboFS = new PboFS(fileTree, archiveManager, options.Prefix);
#if DEBUG
                    ILogger logger = new NullLogger(); //null;
#else
                    ILogger logger = new NullLogger();
#endif
                    pboFS.Mount(options.MountDirectory, Program.MOUNT_OPTIONS, logger);
                    if (writeAbleIsTemporary) 
                        Directory.Delete(options.WriteableDirectory, true);

                    Console.WriteLine("Success");
                }
                catch (DokanException ex)
                {
                    Console.WriteLine("Error: " + ex.Message);
                }
            }
        }
    }
}
