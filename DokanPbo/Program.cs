using CommandLine;
using CommandLine.Text;
using DokanNet;
using System;
using System.Collections.Generic;

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
        static readonly DokanOptions MOUNT_OPTIONS = DokanOptions.DebugMode | DokanOptions.StderrOutput;
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
                    ArchiveManager archiveManager = new ArchiveManager(options.PboDirectories);
                    PboFSTree fileTree = new PboFSTree(archiveManager);
                    PboFS pboFS = new PboFS(fileTree, archiveManager, options.Prefix);
                    pboFS.Mount(options.MountDirectory, Program.MOUNT_OPTIONS);
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
