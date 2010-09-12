﻿#region Copyright
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
using TickZoom.Api;
using TickZoom.FIX;

namespace TickZoom.MBTFIX
{
	public class MBTFIXFilter : FIXFilter {
		private static readonly Log log = Factory.SysLog.GetLogger(typeof(MBTFIXFilter));
		private static readonly bool info = log.IsDebugEnabled;
		private static readonly bool debug = log.IsDebugEnabled;
		private static readonly bool trace = log.IsTraceEnabled;
		private bool isPositionUpdateComplete = false;
		private bool isOrderUpdateComplete = false;
		private bool isRecovered = false;
		private string fixSender = typeof(MBTFIXFilter).Name;
		private Dictionary<long,double> symbolPositionMap = new Dictionary<long,double>();
		
		public void Local(FIXContext context, Packet localPacket)
		{
			var packetFIX = (PacketFIX4_4) localPacket;
			switch( packetFIX.MessageType) {
				case "AF":
					isRecovered = false;
					isOrderUpdateComplete = false;
					if(debug) log.Debug("OrderUpdate Starting.");
					break;
				case "AN":
					isRecovered = false;
					isPositionUpdateComplete = false;
					if(debug) log.Debug("PositionUpdate Starting.");
					break;
				case "G":
				case "D":
					AssertOrderMaximum(context,packetFIX);
					AssertPositionMaximum(context,packetFIX);
					break;
			}
		}
		
		public void Remote(FIXContext context, Packet remotePacket)
		{
			var packetFIX = (PacketFIX4_4) remotePacket;
			switch( packetFIX.MessageType) {
				case "AP":
				case "AO":
					PositionUpdate( context, packetFIX);
					break;
				case "8":
				case "9":
					ExecutionReport( context, packetFIX);
					break;
			}
		}
		
		private void AssertOrderMaximum( FIXContext context, PacketFIX4_4 packet) {
			if( isRecovered) {
				double quantity = packet.OrderQuantity;
				SymbolInfo symbolInfo;
				try {
					symbolInfo = Factory.Symbol.LookupSymbol(packet.Symbol);
				} catch( ApplicationException ex) {
					log.Error("Error looking up " + packet.Symbol + ": " + ex.Message);
					return;
				}
				double maxOrderSize = 5;
				if( quantity > maxOrderSize) {
					CloseWithError(context, packet, "Order size " + quantity + " for " + symbolInfo + " was greater than MaxOrderSize of " + maxOrderSize + " in packet sequence #" + packet.Sequence);
				}
			}
		}
		
		private void AssertPositionMaximum( FIXContext context, PacketFIX4_4 packet) {
			if( isRecovered) {
				int quantity = packet.OrderQuantity;
				SymbolInfo symbolInfo;
				try {
					symbolInfo = Factory.Symbol.LookupSymbol(packet.Symbol);
				} catch( ApplicationException ex) {
					log.Error("Error looking up " + packet.Symbol + ": " + ex.Message);
					return;
				}
				switch(packet.Side) {
					case "1":
						break;
					case "2":
					case "5":
						quantity *= -1;
						break;
					default:
						CloseWithError(context, packet, "Unknown order side " + packet.Side + " in fix message. Unable to perform pre-trade verification.");
						break;
				}
				double position = 0;
				symbolPositionMap.TryGetValue(symbolInfo.BinaryIdentifier,out position);
				position += quantity;
				var maxPositionSize = 5D;
				var positionSize = Math.Abs(position);
				if( positionSize > maxPositionSize) {
					CloseWithError(context, packet, "Position size " + positionSize + " for " + symbolInfo + " was greater than MaxPositionSize of " + maxPositionSize + " in packet sequence #" + packet.Sequence);
				}
			}
		}
		
		private void PositionUpdate( FIXContext context, PacketFIX4_4 packet) {
			if( packet.MessageType == "AO") {
				isPositionUpdateComplete = true;
				if(debug) log.Debug("PositionUpdate Complete.");
				TryEndRecovery();
			} else {
				if( isRecovered) {
					double position = packet.LongQuantity + packet.ShortQuantity;
					SymbolInfo symbolInfo;
					try {
						symbolInfo = Factory.Symbol.LookupSymbol(packet.Symbol);
					} catch( ApplicationException ex) {
						log.Error("Error looking up " + packet.Symbol + ": " + ex.Message);
						return;
					}
					if(debug) log.Debug("PositionUpdate: " + symbolInfo + "=" + position);
					symbolPositionMap[symbolInfo.BinaryIdentifier] = position;
				}
			}
		}
		
		private void ExecutionReport( FIXContext context, PacketFIX4_4 packetFIX) {
			if( packetFIX.Text == "END") {
				isOrderUpdateComplete = true;
				if(debug) log.Debug("ExecutionReport Complete.");
				TryEndRecovery();
			}
		}
		
		private void TryEndRecovery() {
			if( isPositionUpdateComplete && isOrderUpdateComplete) {
				isPositionUpdateComplete = false;
				isOrderUpdateComplete = false;
				isRecovered = true;
			}
		}

		private void CloseWithError(FIXContext context, PacketFIX4_4 packetIn, string message) {
			Packet packet = context.LocalSocket.CreatePacket();
			var fixMsg = new FIXMessage4_4(fixSender,packetIn.Sender);
			TimeStamp timeStamp = TimeStamp.UtcNow;
			fixMsg.SetAccount(packetIn.Account);
			fixMsg.SetText( message);
//			fixMsg.SetSender( 
			fixMsg.AddHeader("j");
			string errorMessage = fixMsg.ToString();
			packet.DataOut.Write(errorMessage.ToCharArray());
			long end = Factory.Parallel.TickCount + 2000;
			if( debug) log.Debug("Writing Error Message: " + packet);
			while( !context.LocalSocket.TrySendPacket(packet)) {
				if( Factory.Parallel.TickCount > end) {
					throw new ApplicationException("Timeout while sending an order.");
				}
				Factory.Parallel.Yield();
			}
			throw new FilterException();
		}
	}
}