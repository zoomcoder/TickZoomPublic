using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using TickZoom.Api;
using TickZoom.TickUtil;

namespace TickZoom.TZData
{
    public class Alter : Command
    {
        public override void Run(string[] args)
        {
            if (args.Length != 1)
            {
                Output("Alter Usage:");
                Usage();
                return;
            }
            AlterFile(args[0]);
        }

        private void AlterFile(string file)
        {
            if (File.Exists(file + ".back"))
            {
                Output("A backup file already exists. Please delete it first at: " + file + ".back");
                return;
            }
            TickFile reader;
            TickFile writer;
            var firstTick = Factory.TickUtil.TickIO();
            var tickIO = Factory.TickUtil.TickIO();
            int count = 0;
            using (reader = Factory.TickUtil.TickFile())
            {
                using (writer = Factory.TickUtil.TickFile())
                {
                    reader.Initialize(file, TickFileMode.Read);

                    writer.Initialize(file + ".temp", TickFileMode.Write);

                    while (reader.TryReadTick(tickIO))
                    {
                        tickIO.IsSimulateTicks = false;
                        writer.WriteTick(tickIO);
                        count++;
                    }
                }
            }
            Output(reader.Symbol + ": Altered " + count + " ticks from " + firstTick.Time + " to " + tickIO.Time);
            MoveFile(file, file + ".back");
            MoveFile(file + ".temp", file);
        }

        public static void MoveFile(string path, string topath)
        {
            var errors = new List<Exception>();
            var errorCount = 0;
            while (errorCount < 300)
            {
                try
                {
                    File.Move(path,topath);
                    errors.Clear();
                    break;
                }
                catch (DirectoryNotFoundException)
                {
                    errors.Clear();
                    break;
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                    Thread.Sleep(100);
                    errorCount++;
                }
            }
            if (errors.Count > 0)
            {
                var ex = errors[errors.Count - 1];
                Factory.Parallel.StackTrace();
                throw new IOException("Can't move " + path + " to " + topath, ex);
            }
        }
        public override string[] UsageLines()
        {
            List<string> lines = new List<string>();
            string name = Assembly.GetEntryAssembly().GetName().Name;
            lines.Add(name + " alter <file>");
            return lines.ToArray();
        }
    }
}