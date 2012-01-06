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

using System.Collections.Generic;
using System.Threading;

namespace TickZoom.Api
{
    public unsafe class TickSyncDirectory
    {
        private SharedMemory directoryInfo;
        private SyncTicksState* syncTicksState;
        private List<SharedMemory> pages = new List<SharedMemory>();
        private int pageSize = 1000;

        public TickSyncDirectory()
        {
            directoryInfo = SharedMemory.Create("TickSyncInfo", sizeof(SyncTicksState));
            syncTicksState = (SyncTicksState*) directoryInfo.BaseAddress;
        }

        public SyncTicksState* SyncTicksState
        {
            get { return syncTicksState; }
        }

        public TickSyncState *GetTickSync( long binarySymbol)
        {
            LoadPages();
            for( int i=0; i<pages.Count; i++)
            {
                var page = pages[i];
                var ptr = (TickSyncState*) page.BaseAddress;
                for( var j=0; j < pageSize; j++, ptr+=2)
                {
                    if( (*ptr).symbolBinaryId == binarySymbol)
                    {
                        return ptr;
                    }
                    else if( Interlocked.CompareExchange(ref (*ptr).symbolBinaryId, binarySymbol, 0 ) == 0)
                    {
                        return ptr;
                    }
                }
            }
            AddPage();
            return GetTickSync(binarySymbol);
        }

        public void LoadPages()
        {
            for (int i = pages.Count+1; i < (*SyncTicksState).pageCount + 1; i++)
            {
                GetPage(i);
            }
        }

        public void GetPage(int value)
        {
            // store 2 X sizeof sync state to hold rollback state.
                 
            var page = SharedMemory.Create("TickSyncMemory" + value, sizeof(TickSyncState) * 2 * pageSize);
            pages.Add(page);
        }

        public void AddPage()
        {
            var value = Interlocked.Increment(ref (*SyncTicksState).pageCount);
            var page = SharedMemory.Create("TickSyncMemory" + value, sizeof (TickSyncState)*pageSize*2);
            pages.Add(page);
        }
    }
}