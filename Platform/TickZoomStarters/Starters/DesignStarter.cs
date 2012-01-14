#region Copyright
/*
 * Software: TickZoom Trading Platform
 * Copyright 2009 M. Wayne Walter
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

#region Copyright
/*
 * Software: TickZoom Trading Platform
 * Copyright 2009 M. Wayne Walter
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
using System.ComponentModel;
using System.Threading;
using TickZoom.Api;

namespace TickZoom.Starters
{
    /// <summary>
    /// This starter is use by the GUI to initialize model for
    /// browsing of properties.
    /// </summary>
    public class DesignStarter : StarterCommon
    {
        Log log = Factory.SysLog.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        TickEngine engine;

        public DesignStarter()
        {
        }

        bool CancelPending
        {
            get
            {
                if (BackgroundWorker != null)
                {
                    return BackgroundWorker.CancellationPending;
                }
                else
                {
                    return false;
                }
            }
        }

        public override void Run(ModelInterface model)
        {
            Factory.Parallel.SetMode(parallelMode);
            Factory.SysLog.ResetConfiguration();
            engine = Factory.Engine.TickEngine("Design");
            engine.MaxBarsBack = 2;
            engine.MaxTicksBack = 2;
            SymbolInfo symbolInfo = Factory.Symbol.LookupSymbol("Design");
            engine.SymbolInfo = new SymbolInfo[] { symbolInfo };
            engine.IntervalDefault = ProjectProperties.Starter.IntervalDefault;
            engine.RunMode = RunMode.Historical;
            engine.DataFolder = DataFolder;
            engine.EnableTickFilter = ProjectProperties.Engine.EnableTickFilter;

            if (CancelPending) return;

            engine.Model = model;

            engine.QueueTask();
            engine.WaitTask();
            var parallel = Factory.Parallel;
            parallel.Dispose();
            while (!parallel.IsFinalized)
            {
                Thread.Sleep(100);
            }
            parallel.Release();

        }

        public override void Wait()
        {
            engine.WaitTask();
            var parallel = Factory.Parallel;
            parallel.Dispose();
            while (!parallel.IsFinalized)
            {
                Thread.Sleep(100);
            }
            parallel.Release();
        }

    }
}