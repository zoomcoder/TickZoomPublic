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
                Output("tzdata " + Usage());
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
            using (var reader = new TickFile())
            using (var writer = Factory.TickUtil.TickWriter(true))
            {
                reader.Initialize(file);

                writer.KeepFileOpen = true;
                writer.Initialize(file + ".temp", reader.Symbol.Symbol);

                var firstTick = Factory.TickUtil.TickIO();
                var tickIO = Factory.TickUtil.TickIO();
                int count = 0;
                try
                {
                    while (true)
                    {
                        reader.TryReadTick(tickIO);
                        tickIO.IsSimulateTicks = false;
                        while (!writer.TryAdd(tickIO))
                        {
                            Thread.Sleep(1);
                        }
                        count++;
                    }
                }
                catch (QueueException ex)
                {
                    if (ex.EntryType != EventType.EndHistorical)
                    {
                        Output("Unexpected QueueException: " + ex);
                    }
                }
                Output(reader.Symbol + ": Altered " + count + " ticks from " + firstTick.Time + " to " + tickIO.Time);
            }
            File.Move(file, file + ".back");
            File.Move(file + ".temp", file);
        }

        public override string[] Usage()
        {
            List<string> lines = new List<string>();
            string name = Assembly.GetEntryAssembly().GetName().Name;
            lines.Add(name + " alter <file>");
            return lines.ToArray();
        }
    }
}