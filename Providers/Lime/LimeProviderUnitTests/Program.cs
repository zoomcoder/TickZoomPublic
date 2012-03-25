#region Copyright
/*
 * Software: TickZoom Trading Platform
 * Copyright 2012 M. Wayne Walter
 * 
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2.1 of the License, or (at your option) any later version.
 * 
 * Business use restricted to 30 days except as otherwise stated in
 * in your Service Level Agreement (SLA).
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, see <http://www.tickzoom.org/wiki/Licenses>
 * or write to Free Software Foundation, Inc., 51 Franklin Street,
 * Fifth Floor, Boston, MA  02110-1301, USA.
 * 
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TickZoom.Api;
using System.Runtime.InteropServices;
using System.IO;

namespace LimeProviderUnitTests
{
    class Program
    {
        static void Main(string[] args)
        {
            DeleteFiles();

            _handler += new EventHandler(Handler);
            SetConsoleCtrlHandler(_handler, true); 

            Console.CancelKeyPress += new ConsoleCancelEventHandler(Console_CancelKeyPress);
            Console.WriteLine("Logged in");

            //_Tests.TestChange();
            Console.WriteLine("Order Buy and Fill");
            Console.WriteLine("Logged out");
            Console.WriteLine();
            Console.WriteLine("Tests Completed: ");
            Console.ReadKey();


        }

        private static void DeleteFiles()
        {
            string[] files = Directory.GetFiles(@"C:\TickZoomHome\DataBase");
            foreach (var f in files)
            {
                if (f.Contains(@"\ClientTest") || f.Contains(@"\MarketTest"))
                    File.Delete(f);
            }

        }

        static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
        }

        // http://stackoverflow.com/questions/474679/capture-console-exit-c-sharp
        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(EventHandler handler, bool add);

        private delegate bool EventHandler(CtrlType sig);
        static EventHandler _handler;

        enum CtrlType
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6
        }

        private static bool Handler(CtrlType sig)
        {
            switch (sig)
            {
                case CtrlType.CTRL_C_EVENT:
                case CtrlType.CTRL_LOGOFF_EVENT:
                case CtrlType.CTRL_SHUTDOWN_EVENT:
                case CtrlType.CTRL_CLOSE_EVENT:
                default:
                    break;
            }
            return false;
        }
    }
}
