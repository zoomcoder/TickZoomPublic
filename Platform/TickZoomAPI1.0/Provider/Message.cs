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
using System.IO;

namespace TickZoom.Api
{
    public interface Message
    {
		void Clear();
		void BeforeWrite();
		void BeforeRead();
		void CreateHeader(int packetCounter);
		void Verify();
		void SetReadableBytes(int bytes);
		bool TrySplit(MemoryStream other);
		bool IsComplete { get; }
        int Id { get; }
		int Remaining {	get; }
		bool HasAny { get; }
		bool IsFull { get; }
		int Position { get; set; }
		int Length { get; }
		long SendUtcTime { get; set; }
        long RecvUtcTime { get; set; }
        BinaryReader DataIn { get; }
		BinaryWriter DataOut { get; }
		MemoryStream Data {	get; }
	}

    public class DisconnectMessage : Message
    {
        private readonly Socket socket;

        public DisconnectMessage(Socket socket)
        {
            this.socket = socket;
        }

        public void Clear()
        {
            throw new NotImplementedException();
        }

        public void BeforeWrite()
        {
            throw new NotImplementedException();
        }

        public void BeforeRead()
        {
            throw new NotImplementedException();
        }

        public void CreateHeader(int packetCounter)
        {
            throw new NotImplementedException();
        }

        public void Verify()
        {
            throw new NotImplementedException();
        }

        public void SetReadableBytes(int bytes)
        {
            throw new NotImplementedException();
        }

        public bool TrySplit(MemoryStream other)
        {
            throw new NotImplementedException();
        }

        public bool IsComplete
        {
            get { throw new NotImplementedException(); }
        }

        public int Id
        {
            get { throw new NotImplementedException(); }
        }

        public int Remaining
        {
            get { throw new NotImplementedException(); }
        }

        public bool HasAny
        {
            get { throw new NotImplementedException(); }
        }

        public bool IsFull
        {
            get { throw new NotImplementedException(); }
        }

        public int Position
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public int Length
        {
            get { throw new NotImplementedException(); }
        }

        public long SendUtcTime
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public long RecvUtcTime
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public BinaryReader DataIn
        {
            get { throw new NotImplementedException(); }
        }

        public BinaryWriter DataOut
        {
            get { throw new NotImplementedException(); }
        }

        public MemoryStream Data
        {
            get { throw new NotImplementedException(); }
        }

        public Socket Socket
        {
            get { return socket; }
        }
    }
}
