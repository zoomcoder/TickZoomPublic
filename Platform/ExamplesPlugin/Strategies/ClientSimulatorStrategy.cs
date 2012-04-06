using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TickZoom.Api;
using TickZoom.Common;
using TickZoom.Statistics;

namespace TickZoom.Examples.Strategies
{
    public class ClientSimulatorStrategy : Strategy {
        private const int _StardardSize = 1000;
        private double tickOffset = 5;
        private const int _LargeTestMultiplier = 3;
        enum Tests {
            FirstTest,      // Must be first

            LimitOrders,
            BuyMarket,
            SellMarket,
            BuyPartialFill,
            SellPartialFill,

            LastTest        // and must be last
        }

        enum TradeState {
            Start,

            MarketSellTrade,
            MarketSellTradeWaitFill,
            MarketSellTradeFilled,
            MarketSellTradeCloseing,
            MarketSellTradeClosed,

            MarketBuyTrade,
            MarketBuyTradeWaitFill,
            MarketBuyTradeFilled,
            MarketBuyTradeCloseing,
            MarketBuyTradeClosed,

            LimitStart,
            LimitWaitFill,
            LimitBuyFilled,
            LimitSellFilled,
            LimitBuyAndSellFilled,
            LimitsClosed

        }
           double multiplier = 1.0D;
        double minimumTick;
        int tradeSize;
        TradeState _State = TradeState.Start;
        Tests _Current = Tests.FirstTest;

        public ClientSimulatorStrategy()
        {
            Performance.GraphTrades = true;
            Performance.Equity.GraphEquity = true;
            ExitStrategy.ControlStrategy = false;
        }

        private void NextTest() {
            if ( _Current < Tests.LastTest ) {
                _Current = _Current + 1;
                _State = TradeState.Start;
                if ( _Current == Tests.LastTest)
                    Log.Notice("Tests Completed");
                Log.NoticeFormat("Starting test {0}", _Current);
            }
        }
		
        public override void OnInitialize()
        {
            tradeSize = Data.SymbolInfo.Level2LotSize * 10;
            minimumTick = multiplier * Data.SymbolInfo.MinimumTick;
            //ExitStrategy.BreakEven = 30 * minimumTick;
            //ExitStrategy.StopLoss = 45 * minimumTick;
        }
        public override bool OnProcessTick(Tick tick) {
            bool retCode = false;
            switch (_Current) {
                case Tests.FirstTest:
                    NextTest();
                    break;
                case Tests.BuyMarket:
                    retCode = BuyMarketTest(tick, _StardardSize);
                    if (_State == TradeState.MarketBuyTradeClosed)
                        NextTest();
                    break;
                case Tests.SellMarket:
                    retCode = SellMarketTest(tick, _StardardSize);
                    if (_State == TradeState.MarketSellTradeClosed)
                        NextTest();
                    break;
                case Tests.LimitOrders:
                    retCode = LimeOrderTest(tick, _StardardSize);
                    if ( _State == TradeState.LimitBuyAndSellFilled )
                        NextTest();
                    break;
                case Tests.BuyPartialFill:
                    retCode = BuyMarketTest(tick, _StardardSize*_LargeTestMultiplier);
                    if (_State == TradeState.MarketSellTradeClosed)
                        NextTest();
                    break;
                case Tests.SellPartialFill:
                    retCode = SellMarketTest(tick, _StardardSize*_LargeTestMultiplier);
                    if (_State == TradeState.MarketSellTradeClosed)
                        NextTest();
                    break;
                case Tests.LastTest:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return retCode;
        }

        private bool LimeOrderTest(Tick tick, int size) {
            var midPoint = (tick.Ask + tick.Bid) / 2;
            var bid = midPoint - Data.SymbolInfo.MinimumTick * tickOffset;
            var ask = midPoint + Data.SymbolInfo.MinimumTick * tickOffset;
            switch (_State) {
                case TradeState.Start:
                    _State = TradeState.LimitStart;
                    break;
                case TradeState.LimitStart:
                    Orders.Change.ActiveNow.BuyLimit(bid, size);
                    Orders.Change.ActiveNow.SellLimit(ask, size);
                    _State = TradeState.LimitWaitFill;
                    break;
                case TradeState.LimitWaitFill:
                    break;
                case TradeState.LimitSellFilled:
                    Orders.Change.ActiveNow.BuyLimit(bid, size);
                    break;
                case TradeState.LimitBuyFilled:
                    Orders.Change.ActiveNow.SellLimit(ask, size);
                    break;
                case TradeState.LimitBuyAndSellFilled:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();

            }
            return true;
        }

        public bool BuyMarketTest(Tick tick, int size)
        {
            switch (_State)
            {
                case TradeState.Start:
                    _State = TradeState.MarketBuyTrade;
                    break;
                case TradeState.MarketBuyTrade:
                    Log.Notice("Market Buy");
                    Orders.Enter.ActiveNow.BuyMarket(size);
                    _State = TradeState.MarketBuyTradeWaitFill;
                    Log.Notice("MarketBuy Trade Placed");
                    break;
                case TradeState.MarketBuyTradeWaitFill:
                    break;
                case TradeState.MarketBuyTradeFilled:
                    if (Position.HasPosition)
                    {
                        Log.Notice("Closing MarketBuy Trade");
                        Orders.Exit.ActiveNow.GoFlat();
                        _State = TradeState.MarketBuyTradeCloseing;
                    }
                    break;
                case TradeState.MarketBuyTradeCloseing:
                    break;
                case TradeState.MarketBuyTradeClosed:
                    break;
            }
            return true;
        }
      
        public bool SellMarketTest(Tick tick,int size)
        {
            switch (_State)
            {
                case TradeState.Start:
                    _State = TradeState.MarketSellTrade;
                    break;
                case TradeState.MarketSellTrade:
                    Log.Notice("Market Sell");
                    Orders.Enter.ActiveNow.SellMarket(size);
                    _State = TradeState.MarketSellTradeWaitFill;
                    Log.Notice("MarketSell Trade Placed");
                    break;
                case TradeState.MarketSellTradeWaitFill:
                    break;
                case TradeState.MarketSellTradeFilled:
                    if (Position.HasPosition)
                    {
                        Log.Notice("Closing MarketSell Trade");
                        Orders.Exit.ActiveNow.GoFlat();
                        _State = TradeState.MarketSellTradeCloseing;
                    }
                    break;
                case TradeState.MarketSellTradeCloseing:
                    break;
                case TradeState.MarketSellTradeClosed:
                    break;
            }
            return true;
        }

       public override void OnEnterTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            switch (_State) {
                case TradeState.MarketSellTradeWaitFill:
                    Log.Notice("MarketSell Trade Filled");
                    _State = TradeState.MarketSellTradeFilled;
                    break;
                case TradeState.MarketBuyTradeWaitFill:
                    Log.Notice("Market Buy Trade Filled");
                    _State = TradeState.MarketBuyTradeFilled;
                    break;
                case TradeState.LimitWaitFill:
                    if (Position.IsLong)
                        _State = TradeState.LimitBuyFilled;
                    else
                        _State = TradeState.LimitSellFilled; ;
                    break;
                case TradeState.LimitBuyFilled:
                case TradeState.LimitSellFilled:
                    _State = TradeState.LimitBuyAndSellFilled;
                    break;
                default:
                    throw new ApplicationException("Trade Entered on unknown state");
            }

            base.OnEnterTrade(comboTrade, fill, filledOrder);
        }

       public override void OnExitTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
       {
           switch (_State)
           {
               case TradeState.MarketSellTradeCloseing:
                   Log.Notice("MarketSell Trade Closed");
                   _State = TradeState.MarketSellTradeClosed;
                   break;
               case TradeState.MarketBuyTradeCloseing:
                   Log.Notice("MarketBuy Trade Closed");
                   _State = TradeState.MarketBuyTradeClosed;
                   break;
               case TradeState.LimitBuyFilled:
               case TradeState.LimitSellFilled:
                   _State = TradeState.LimitBuyAndSellFilled;
                   break;
               default:
                   throw new ApplicationException("Trade exited on unknown state");
           }
           base.OnExitTrade(comboTrade, fill, filledOrder);
       }

    }
}
