using CommandLine;
using CommandLine.Text;
using DokanNet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using DokanNet.Logging;
using DokanPbo;

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

        //https://social.msdn.microsoft.com/Forums/vstudio/en-US/707e9ae1-a53f-4918-8ac4-62a1eddb3c4a/detecting-console-application-exit-in-c
        [DllImport("Kernel32")]
        public static extern bool SetConsoleCtrlHandler(HandlerRoutine Handler, bool Add);

        // A delegate type to be used as the handler routine
        // for SetConsoleCtrlHandler.
        public delegate bool HandlerRoutine(CtrlTypes CtrlType);

        // An enumerated type for the control messages
        // sent to the handler routine.
        public enum CtrlTypes
        {
        }

        private static bool ConsoleCtrlCheck(CtrlTypes ctrlType)
        {
            if (deleteTempDirOnClose != null)
                Directory.Delete(deleteTempDirOnClose, true);
            return true;
        }

        private static string deleteTempDirOnClose;



#if DEBUG
        static readonly DokanOptions MOUNT_OPTIONS = DokanOptions.DebugMode | DokanOptions.StderrOutput | DokanOptions.FixedDrive;
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

                    if (options.WriteableDirectory == null)
                    {
                        Console.WriteLine("Creating temporary write directory...");
                        options.WriteableDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                        //#TODO can throw exception and die if it creates a existing folder by accident
                        Directory.CreateDirectory(options.WriteableDirectory);

                        //Need to register handler to catch console exit to delete directory at end
                        SetConsoleCtrlHandler(ConsoleCtrlCheck, true);
                        deleteTempDirOnClose = options.WriteableDirectory;
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
                    pboFS.Mount(options.MountDirectory, Program.MOUNT_OPTIONS,8, logger);
                    Console.WriteLine("Success");
                }
                catch (DokanException ex)
                {
                    Console.WriteLine("Error: " + ex.Message);
                }
            }
        }

        private static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            throw new NotImplementedException();
        }
    }
}
