using BinanceExchange.API.Client;
using BinanceExchange.API.Enums;
using BinanceExchange.API.Market;
using BinanceExchange.API.Models.Request;
using BinanceExchange.API.Models.Response.Error;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BinanceTradeBot
{
    class Program
    {
        const string API_KEY = "3oFOTwIdIUGiq5TSJF5fHxOMKA32uHhOwwSLNWlCCClhMt2DKPCYbsxEJrG1x11X";
        const string SECRET_KEY = "uTFjolfgbaaE1aqTs36yo9YRO62YHo6ZLMXTo4x4OcCMWKhTch0x4bltLX6zC484";
        const decimal LOT_STEP = 0.00001000M;
        const int MAX_HISTORY_TRADES = 10;
        const decimal MIN_LOT_USDT = 10M;
        const int ORDER_PRICE_STEP = 250;
        const int TIMEOUT = 1000 * 30;
        const string LOG_FILE = "log.csv";
        const string ORDERS_FILE = "orders.csv";

        static async Task Main(string[] args)
        {
            do
            {

                var client = new BinanceClient(new ClientConfiguration()
                {
                    ApiKey = API_KEY,
                    SecretKey = SECRET_KEY
                });

                await client.TestConnectivity();

                CreateOrderRequest createOrderRequest = null;

                try
                {
                    do
                    {
                        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");

                        //Get all orders
                        var allOrdersRequest = new AllOrdersRequest()
                        {
                            Symbol = TradingPairSymbols.USDTPairs.BTC_USDT
                        };

                        var allOrders = await client.GetAllOrders(allOrdersRequest);

                        var allTradeRequest = new AllTradesRequest()
                        {
                            Symbol = TradingPairSymbols.USDTPairs.BTC_USDT
                        };

                        var trades = await client.GetAccountTrades(allTradeRequest);

                        var lastTrades = trades.OrderByDescending(t => t.Time).Take(MAX_HISTORY_TRADES);

                        // Get daily ticker
                        var dailyTicker = await client.GetDailyTicker(TradingPairSymbols.USDTPairs.BTC_USDT);

                        var BTCPrice = dailyTicker.LastPrice;

                        foreach (var lastTrade in lastTrades)
                        {
                            if (lastTrade != null)
                            {
                                var tradeOrder = allOrders.Find(o => o.OrderId == lastTrade.OrderId);
                                if (tradeOrder != null)
                                {
                                    // Get account information
                                    var accountInformation = await client.GetAccountInformation();

                                    var amountBTC = accountInformation.Balances.First(b => b.Asset == "BTC").Free;
                                    var amountUSD = accountInformation.Balances.First(b => b.Asset == "USDT").Free;

                                    if (amountBTC * BTCPrice >= MIN_LOT_USDT || amountUSD >= MIN_LOT_USDT)
                                    {
                                        //create sell orders
                                        if (lastTrade.Quantity * BTCPrice < MIN_LOT_USDT / 10) //broken order, skip it
                                        {
                                            continue;
                                        }
                                        var fixedAmount = lastTrade.Quantity;
                                        while (fixedAmount < amountBTC && fixedAmount * BTCPrice < MIN_LOT_USDT)
                                        {
                                            fixedAmount += LOT_STEP;
                                        }

                                        if (tradeOrder.Side == OrderSide.Sell)
                                        {
                                            while (fixedAmount * BTCPrice > amountUSD)
                                            {
                                                fixedAmount -= BTCPrice * LOT_STEP;
                                            }
                                            createOrderRequest = new CreateOrderRequest()
                                            {
                                                Price = lastTrade.Price - ORDER_PRICE_STEP,
                                                TimeInForce = TimeInForce.GTC,
                                                Quantity = fixedAmount,
                                                Side = OrderSide.Buy,
                                                Symbol = TradingPairSymbols.USDTPairs.BTC_USDT,
                                                Type = OrderType.Limit,
                                            };
                                        }
                                        else if (tradeOrder.Side == OrderSide.Buy)
                                        {
                                            while (fixedAmount > amountBTC)
                                            {
                                                fixedAmount -= LOT_STEP;
                                            }
                                            createOrderRequest = new CreateOrderRequest()
                                            {
                                                Price = lastTrade.Price + ORDER_PRICE_STEP,
                                                TimeInForce = TimeInForce.GTC,
                                                Quantity = fixedAmount,
                                                Side = OrderSide.Sell,
                                                Symbol = TradingPairSymbols.USDTPairs.BTC_USDT,
                                                Type = OrderType.Limit,
                                            };
                                        }

                                        if (fixedAmount == 0)
                                        {
                                            continue;
                                        }

                                        var message = $"{DateTime.Now:G},{createOrderRequest.Side},{createOrderRequest.Price:0},{createOrderRequest.Quantity}\n";
                                        Console.WriteLine(message);
                                        File.AppendAllText(ORDERS_FILE, message);

                                        Thread.Sleep(1000);
                                    }
                                }
                            }
                        }

                        File.AppendAllText(LOG_FILE, $"{DateTime.Now:G},{BTCPrice:0},{dailyTicker.TradeCount:0},{dailyTicker.Volume:0}\n");

                        Thread.Sleep(TIMEOUT);
                    }
                    while (true);
                }
                catch (BinanceBadRequestException ex)
                {
                    var message = $"{DateTime.Now:G}: {ex.ErrorDetails.Message}\n{JsonConvert.SerializeObject(createOrderRequest)}\n";
                    Console.WriteLine(message);
                    File.AppendAllText(ORDERS_FILE, message);
                }
                catch (Exception ex)
                {
                    var message = $"{DateTime.Now:G}: {ex.Message} {ex.StackTrace}\n{JsonConvert.SerializeObject(createOrderRequest)}\n";
                    Console.WriteLine(message);
                    File.AppendAllText(ORDERS_FILE, message);
                }

                Console.WriteLine($"{DateTime.Now:G}: Wait for {TIMEOUT * 3} after exception");
                Thread.Sleep(TIMEOUT * 3);

            } while (true);
        }
    }
}
