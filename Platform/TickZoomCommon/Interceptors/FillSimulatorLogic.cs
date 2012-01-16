using System;
using TickZoom.Api;

namespace TickZoom.Interceptors
{
    public delegate void FillSimulatorCallback(Order order, double price, Tick tick);

    public class FillSimulatorLogic : LogAware
    {
        private Log log;
        private volatile bool debug;
        private volatile bool trace;
        private volatile bool verbose;
        private FillSimulatorCallback fillCallback;
        public void RefreshLogLevel()
        {
            if (log != null)
            {
                debug = log.IsDebugEnabled;
                trace = log.IsTraceEnabled;
                verbose = log.IsVerboseEnabled;
            }
        }
        private SymbolInfo symbol;
        private string name;
        public FillSimulatorLogic(string name, SymbolInfo symbol, FillSimulatorCallback callback)
        {
            this.symbol = symbol;
            this.name = name;
            fillCallback = callback;
            log = Factory.SysLog.GetLogger(typeof(FillSimulatorPhysical).FullName + "." + symbol.Symbol.StripInvalidPathChars() + "." + name);
            RefreshLogLevel();
            log.Register(this);
            limitOrderQuoteSimulation = symbol.LimitOrderQuoteSimulation;
            limitOrderTradeSimulation = symbol.LimitOrderTradeSimulation;
        }
        private LimitOrderQuoteSimulation limitOrderQuoteSimulation;
        private LimitOrderTradeSimulation limitOrderTradeSimulation;
        public void TryFillOrder(Order order, Tick tick)
        {
            switch (order.Type)
            {
                case OrderType.SellMarket:
                    ProcessSellMarket(order, tick);
                    break;
                case OrderType.SellStop:
                    ProcessSellStop(order, tick);
                    break;
                case OrderType.SellLimit:
                    if (tick.IsTrade && limitOrderTradeSimulation != LimitOrderTradeSimulation.None)
                    {
                        ProcessSellLimitTrade(order, tick);
                    }
                    else if (tick.IsQuote && limitOrderQuoteSimulation != LimitOrderQuoteSimulation.None)
                    {
                        ProcessSellLimitQuote(order, tick);
                    }
                    break;
                case OrderType.BuyMarket:
                    ProcessBuyMarket(order, tick);
                    break;
                case OrderType.BuyStop:
                    ProcessBuyStop(order, tick);
                    break;
                case OrderType.BuyLimit:
                    if (tick.IsTrade && limitOrderTradeSimulation != LimitOrderTradeSimulation.None)
                    {
                        ProcessBuyLimitTrade(order, tick);
                    }
                    else if (tick.IsQuote && limitOrderQuoteSimulation != LimitOrderQuoteSimulation.None)
                    {
                        ProcessBuyLimitQuote(order, tick);
                    }
                    break;
            }
        }
        private bool ProcessBuyStop(Order order, Tick tick)
        {
            bool retVal = false;
            long price = tick.IsQuote ? tick.lAsk : tick.lPrice;
            if (price >= order.Price.ToLong())
            {
                fillCallback(order, price.ToDouble(), tick);
                retVal = true;
            }
            return retVal;
        }

        private bool ProcessSellStop(Order order, Tick tick)
        {
            bool retVal = false;
            long price = tick.IsQuote ? tick.lBid : tick.lPrice;
            if (price <= order.Price.ToLong())
            {
                fillCallback(order, price.ToDouble(), tick);
                retVal = true;
            }
            return retVal;
        }

        private bool ProcessBuyMarket(Order order, Tick tick)
        {
            if (!tick.IsQuote && !tick.IsTrade)
            {
                throw new ApplicationException("tick w/o either trade or quote data? " + tick);
            }
            var price = tick.IsQuote ? tick.Ask : tick.Price;
            fillCallback(order, price, tick);
            if (debug) log.Debug("Filling " + order.Type + " at " + price + " using tick UTC time " + tick.UtcTime + "." + tick.UtcTime.Microsecond);
            return true;
        }

        private bool ProcessBuyLimitTrade(Order order, Tick tick)
        {
            var result = false;
            var orderPrice = order.Price.ToLong();
            var fillPrice = 0D;
            switch (limitOrderTradeSimulation)
            {
                case LimitOrderTradeSimulation.TradeTouch:
                    if (tick.lPrice <= orderPrice)
                    {
                        fillPrice = tick.Price;
                        result = true;
                    }
                    break;
                case LimitOrderTradeSimulation.TradeThrough:
                    if (tick.lPrice < orderPrice)
                    {
                        fillPrice = order.Price;
                        result = true;
                    }
                    break;
                default:
                    throw new InvalidOperationException("Unknown limit order trade simulation: " + limitOrderTradeSimulation);
            }
            if (result)
            {
                fillCallback(order, fillPrice, tick);
            }
            return result;
        }

        private bool ProcessBuyLimitQuote(Order order, Tick tick)
        {
            var orderPrice = order.Price.ToLong();
            var result = false;
            var fillPrice = 0D;
            switch (limitOrderQuoteSimulation)
            {
                case LimitOrderQuoteSimulation.SameSideQuoteTouch:
                    var bid = Math.Min(tick.lBid, tick.lAsk);
                    if (bid <= orderPrice)
                    {
                        fillPrice = order.Price;
                        result = true;
                    }
                    break;
                case LimitOrderQuoteSimulation.SameSideQuoteThrough:
                    bid = Math.Min(tick.lBid, tick.lAsk);
                    if (bid < orderPrice)
                    {
                        fillPrice = order.Price;
                        result = true;
                    }
                    break;
                case LimitOrderQuoteSimulation.OppositeQuoteTouch:
                    if (tick.lAsk <= orderPrice)
                    {
                        fillPrice = tick.Ask;
                        result = true;
                    }
                    break;
                case LimitOrderQuoteSimulation.OppositeQuoteThrough:
                    if (tick.lAsk < orderPrice)
                    {
                        fillPrice = order.Price;
                        result = true;
                    }
                    break;
                default:
                    throw new InvalidOperationException("Unknown limit order quote simulation: " + limitOrderQuoteSimulation);
            }
            if (result)
            {
                if (debug) log.Debug("Filling " + order.Type + " with " + limitOrderQuoteSimulation + " at ask " + tick.Ask + " / bid " + tick.Bid + " at " + tick.Time);
                fillCallback(order, fillPrice, tick);
            }
            return result;
        }

        private bool ProcessSellLimitTrade(Order order, Tick tick)
        {
            var result = false;
            var orderPrice = order.Price.ToLong();
            var fillPrice = 0D;
            switch (limitOrderTradeSimulation)
            {
                case LimitOrderTradeSimulation.TradeTouch:
                    if (tick.lPrice >= orderPrice)
                    {
                        fillPrice = tick.Price;
                        result = true;
                    }
                    break;
                case LimitOrderTradeSimulation.TradeThrough:
                    if (tick.lPrice > orderPrice)
                    {
                        fillPrice = order.Price;
                        result = true;
                    }
                    break;
                default:
                    throw new InvalidOperationException("Unknown limit order trade simulation: " + limitOrderTradeSimulation);
            }
            if (result)
            {
                fillCallback(order, fillPrice, tick);
            }
            return true;
        }

        private bool ProcessSellLimitQuote(Order order, Tick tick)
        {
            var orderPrice = order.Price.ToLong();
            var result = false;
            var fillPrice = 0D;
            switch (limitOrderQuoteSimulation)
            {
                case LimitOrderQuoteSimulation.SameSideQuoteTouch:
                    var ask = Math.Max(tick.lAsk, tick.lBid);
                    if (ask >= orderPrice)
                    {
                        fillPrice = order.Price;
                        result = true;
                    }
                    break;
                case LimitOrderQuoteSimulation.SameSideQuoteThrough:
                    ask = Math.Max(tick.lAsk, tick.lBid);
                    if (ask > orderPrice)
                    {
                        fillPrice = order.Price;
                        result = true;
                    }
                    break;
                case LimitOrderQuoteSimulation.OppositeQuoteTouch:
                    if (tick.lBid >= orderPrice)
                    {
                        fillPrice = tick.Bid;
                        result = true;
                    }
                    break;
                case LimitOrderQuoteSimulation.OppositeQuoteThrough:
                    if (tick.lBid > orderPrice)
                    {
                        fillPrice = order.Price;
                        result = true;
                    }
                    break;
                default:
                    throw new InvalidOperationException("Unknown limit order quote simulation: " + limitOrderQuoteSimulation);
            }
            if (result)
            {
                if (debug) log.Debug("Filling " + order.Type + " with " + limitOrderQuoteSimulation + " at ask " + tick.Ask + " / bid " + tick.Bid + " at " + tick.Time);
                fillCallback(order, fillPrice, tick);
                result = true;
            }
            return result;
        }

        private bool ProcessSellMarket(Order order, Tick tick)
        {
            if (!tick.IsQuote && !tick.IsTrade)
            {
                throw new ApplicationException("tick w/o either trade or quote data? " + tick);
            }
            double price = tick.IsQuote ? tick.Bid : tick.Price;
            fillCallback(order, price, tick);
            if (debug) log.Debug("Filling " + order.Type + " at " + price + " using tick UTC time " + tick.UtcTime + "." + tick.UtcTime.Microsecond);
            return true;
        }
    }
}