/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using QuantConnect.Configuration;
using QuantConnect.Data.Market;
using QuantConnect.Logging;
using QuantConnect.Util;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QuantConnect.ToolBox.BitmexDownloader
{
    public static class BitmexDownloaderProgram
    {
        /// <summary>
        /// Primary entry point to the program.
        /// </summary>
        public static void BitmexDownloader(IList<string> tickers, string resolution, DateTime fromDate, DateTime toDate)
        {
            if (resolution.IsNullOrEmpty() || tickers.IsNullOrEmpty())
            {
                Console.WriteLine("BitmexDownloader ERROR: '--tickers=' or '--resolution=' parameter is missing");
                Console.WriteLine("--tickers=eg XBTUSD");
                Console.WriteLine("--resolution=Second/Minute/Hour/Daily/All");
                Environment.Exit(1);
            }
            try
            {
                var allResolutions = resolution.ToLower() == "all";
                var castResolution = allResolutions ? Resolution.Minute : (Resolution)Enum.Parse(typeof(Resolution), resolution);

                // Load settings from config.json
                var dataDirectory = Config.Get("data-folder", "../../../Data");

                using (var downloader = new BitmexDataDownloader())
                {
                    foreach (var ticker in tickers)
                    {
                        // Download the data
                        var startDate = fromDate;
                        var symbol = downloader.GetSymbol(ticker);
                        var data = downloader.Get(symbol, castResolution, fromDate, toDate);
                        var bars = data.Cast<TradeBar>().ToList();

                        // Save the data (single resolution)
                        var writer = new LeanDataWriter(castResolution, symbol, dataDirectory);
                        writer.Write(bars);

                        if (allResolutions && bars.Any())
                        {
                            // Save the data (other resolutions)
                            foreach (var res in new[] { Resolution.Hour, Resolution.Daily })
                            {
                                // exclude in-progress (incomplete) bins for the current time period
                                var lastPeriod = bars.Last().EndTime.RoundDown(res.ToTimeSpan());
                                var resData = downloader.AggregateBars(
                                    symbol,
                                    bars.Where(bar => bar.EndTime <= lastPeriod),
                                    res.ToTimeSpan());

                                writer = new LeanDataWriter(res, symbol, dataDirectory);
                                writer.Write(resData);
                            }
                        }
                    }
                }
            }
            catch (Exception err)
            {
                Log.Error(err);
            }
        }
    }
}
