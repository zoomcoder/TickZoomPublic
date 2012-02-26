using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading;
using TickZoom.Api;
using TickZoom.TickUtil;

namespace TickZoom.TZData
{
    public class Export : Command
    {
        string assemblyName;
        string dataFolder = "DataCache";

        // Log log = Factory.SysLog.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        TickFile reader = Factory.TickUtil.TickFile();

        private TimeStamp startTime = TimeStamp.MinValue;
        private TimeStamp endTime = TimeStamp.MaxValue;

        public override void Run(string[] args)
        {
            string symbolString;

            if (args.Length == 1)
            {
                string filePath = args[0];
                reader.Initialize(filePath, TickFileMode.Read);
            }
            else if (args.Length == 2)
            {
                string filePath = args[0];
                symbolString = args[1];
                reader.Initialize(filePath, symbolString, TickFileMode.Read);
            }
            else if( args.Length == 3)
            {
                string filePath = args[0];
                reader.Initialize(filePath,TickFileMode.Read);
                startTime = new TimeStamp(args[1]);
                endTime = new TimeStamp(args[2]);
            }
            else if (args.Length == 4)
            {
                string filePath = args[0];
                symbolString = args[1];
                reader.Initialize(filePath, symbolString, TickFileMode.Read);
                startTime = new TimeStamp(args[2]);
                endTime = new TimeStamp(args[3]);
            }
            else
            {
                Output("Export Usage:");
                Usage();
                return;
            }
            ReadFile();
        }

        public void ReadFile()
        {
            TickIO tickIO = Factory.TickUtil.TickIO();
            try
            {
                while (reader.TryReadTick(tickIO)) 
                {
                    if (tickIO.UtcTime > endTime)
                    {
                        break;
                    }
                    if( tickIO.UtcTime > startTime)
                    {
                        Output(tickIO.ToString());
                    }
                }
            }
            catch (QueueException ex)
            {
                if (ex.EntryType != EventType.EndHistorical)
                {
                    throw;
                }
            }
        }

        public override string[] UsageLines()
        {
            return new string[] { AssemblyName + " export <file> [<symbol>] [<starttimestamp> <endtimestamp>]" };
        }

        public string AssemblyName
        {
            get { return assemblyName; }
            set { assemblyName = value; }
        }

        public string DataFolder
        {
            get { return dataFolder; }
            set { dataFolder = value; }
        }
    }
}