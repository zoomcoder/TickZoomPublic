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
using System.ComponentModel;
using System.Drawing.Design;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

using TickZoom.Api;
using TickZoom.Properties;

namespace TickZoom.Symbols
{
	[Serializable]
	public class SymbolProperties : PropertiesBase, ISymbolProperties
	{
		private Elapsed sessionStart = new Elapsed( 8, 0, 0);
		private Elapsed sessionEnd = new Elapsed( 16, 30, 0);
	    private PartialFillSimulation partialFillSimulation = PartialFillSimulation.PartialFillsTillComplete;
		private bool simulateTicks;
		private string symbol;
		private double minimumTick;
		private double fullPointValue;
		private int level2LotSize = 1;
		private double level2Increment;
		private int level2LotSizeMinimum;
		private long binaryIdentifier;
		private SymbolInfo universalSymbol;
		private int chartGroup;
		private QuoteType quoteType = QuoteType.Level1;
		private TimeAndSales timeAndSales = TimeAndSales.ActualTrades;
		private string displayTimeZone;
		private string timeZone;
		private bool useSyntheticMarkets = true;
		private bool useSyntheticLimits = true;
		private bool useSyntheticStops = true;
	    private ProfitLoss profitLoss;
		private string commission = "default";
		private string fees = "default";
		private string slippage = "default";
		private string destination = "default";
		private double maxPositionSize = double.MaxValue;
		private double maxOrderSize = double.MaxValue;
		private double maxValidPrice = double.MaxValue;
        private int minimumTickPrecision;
	    private FIXSimulationType fixSimulationType;
        private LimitOrderQuoteSimulation _limitOrderQuoteSimulation = LimitOrderQuoteSimulation.OppositeQuoteTouch;
	    private LimitOrderTradeSimulation _limitOrderTradeSimulation = LimitOrderTradeSimulation.TradeTouch;
	    private OptionChain optionChain = OptionChain.None;
	    private TimeInForce timeInForce;
	    private string symbolFile;
        private bool _disableRealtimeSimulation = false;

        public SymbolProperties()
        {
             profitLoss = new ProfitLossDefault(this);
        }

		public SymbolProperties Copy()
	    {
	    	SymbolProperties result;
	    	
	        using (var memory = new MemoryStream())
	        {
	            var formatter = new BinaryFormatter();
	            formatter.Serialize(memory, this);
	            memory.Position = 0;
	
	            result = (SymbolProperties)formatter.Deserialize(memory);
	            memory.Close();
	        }
	
	        return result;
	    }
	    
		public override string ToString()
		{
			return symbol == null ? "empty" : symbol;
		}
		
		[Obsolete("Please create your data with the IsSimulateTicks flag set to true instead of this property.",true)]
		public bool SimulateTicks {
			get { return simulateTicks; }
			set { simulateTicks = value; }
		}
		
		public Elapsed SessionEnd {
			get { return sessionEnd; }
			set { sessionEnd = value; }
		}
		
		public Elapsed SessionStart {
			get { return sessionStart; }
			set { sessionStart = value; }
		}
		
		public double MinimumTick {
			get { return minimumTick; }
			set {
                minimumTick = value; 
                SetPrecision();
            }
		}

        private void SetPrecision()
        {
            var minimumTick = this.minimumTick;
            minimumTickPrecision = 0;
            while ((long)minimumTick != minimumTick)
            {
                minimumTick *= 10;
                minimumTickPrecision ++;
            }
        }

		public double FullPointValue {
			get { return fullPointValue; }
			set { fullPointValue = value; }
		}
	
		public string Symbol {
			get { return symbol; }
			set { symbol = value; }
		}
		
		public int Level2LotSize {
			get { return level2LotSize; }
			set { level2LotSize = value; }
		}
		
		public double Level2Increment {
			get { return level2Increment; }
			set { level2Increment = value; }
		}
		
		public SymbolInfo UniversalSymbol {
			get { return universalSymbol; }
			set { universalSymbol = value; }
		}
		
		public long BinaryIdentifier {
			get { return binaryIdentifier; }
			set { binaryIdentifier = value; }
		}
		
		public override bool Equals(object obj)
		{
			return obj is SymbolInfo && ((SymbolInfo)obj).BinaryIdentifier == binaryIdentifier;
		}
	
		public override int GetHashCode()
		{
			return binaryIdentifier.GetHashCode();
		}
		
		public QuoteType QuoteType {
			get { return quoteType; }
			set { quoteType = value; }
		}
		
		public string DisplayTimeZone {
			get { return displayTimeZone; }
			set { displayTimeZone = value; }
		}

		public string TimeZone {
			get { return timeZone; }
			set { timeZone = value; }
		}
		
		public bool UseSyntheticMarkets {
			get { return useSyntheticMarkets; }
			set { useSyntheticMarkets = value; }
		}
		
		public bool UseSyntheticLimits {
			get { return useSyntheticLimits; }
			set { useSyntheticLimits = value; }
		}
		
		public bool UseSyntheticStops {
			get { return useSyntheticStops; }
			set { useSyntheticStops = value; }
		}
		
		public ProfitLoss ProfitLoss {
			get { return profitLoss; }
			set { profitLoss = value; }
		}
		
		public string Destination {
			get { return destination; }
			set { destination = value; }
		}

		public string Fees {
			get { return fees; }
			set { fees = value; }
		}
		
		public string Commission {
			get { return commission; }
			set { commission = value; }
		}
		
		public string Slippage {
			get { return slippage; }
			set { slippage = value; }
		}
		
		public double MaxPositionSize {
			get { return maxPositionSize; }
			set { maxPositionSize = value; }
		}
		
		public double MaxOrderSize {
			get { return maxOrderSize; }
			set { maxOrderSize = value; }
		}
		
		public TimeAndSales TimeAndSales {
			get { return timeAndSales; }
			set { timeAndSales = value; }
		}

		public int ChartGroup {
			get { return chartGroup; }
			set { chartGroup = value; }
		}
		
		public int Level2LotSizeMinimum {
			get { return level2LotSizeMinimum; }
			set { level2LotSizeMinimum = value; }
		}		
		
		public double MaxValidPrice {
			get { return maxValidPrice; }
			set { maxValidPrice = value; }
		}
		
		public bool Equals(SymbolInfo other)
		{
			return this.binaryIdentifier == other.BinaryIdentifier;
		}

        public LimitOrderQuoteSimulation LimitOrderQuoteSimulation
	    {
	        get { return _limitOrderQuoteSimulation; }
	        set { _limitOrderQuoteSimulation = value; }
	    }

        public LimitOrderTradeSimulation LimitOrderTradeSimulation
	    {
	        get { return _limitOrderTradeSimulation; }
	        set { _limitOrderTradeSimulation = value; }
	    }

	    public int MinimumTickPrecision
	    {
	        get { return minimumTickPrecision; }
	    }

	    public FIXSimulationType FixSimulationType
	    {
	        get { return fixSimulationType; }
	        set { fixSimulationType = value; }
	    }

	    public OptionChain OptionChain
	    {
	        get { return optionChain; }
	        set { optionChain = value; }
	    }

	    public TimeInForce TimeInForce
	    {
	        get { return timeInForce; }
	        set { timeInForce = value; }
	    }

	    public string SymbolFile
	    {
	        get {
                if( symbolFile == null)
                {
                    return symbol;
                }
                else
                {
                    
                }
                return symbolFile;
            }
	        set { symbolFile = value; }
	    }

	    public PartialFillSimulation PartialFillSimulation
	    {
	        get { return partialFillSimulation; }
	        set { partialFillSimulation = value; }
	    }

        #region SymbolInfo Members

	    public bool DisableRealtimeSimulation {
	        get { return _disableRealtimeSimulation; }
	        set { _disableRealtimeSimulation = value; }
	    }

	    #endregion
    }
}
