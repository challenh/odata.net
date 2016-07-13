//---------------------------------------------------------------------
// <copyright file="ODataReaderFeedTests.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------

namespace Microsoft.OData.Performance
{
    using System;
    using System.IO;
    using System.Linq;
    using global::Xunit;
    using Microsoft.OData.Edm;
    using Microsoft.OData;
    using Microsoft.Xunit.Performance;

    public class ODataReaderFeedTests
    {
        private static readonly IEdmModel Model = TestUtils.GetAdventureWorksModel();
        private static readonly IEdmEntitySet TestEntitySet = Model.EntityContainer.FindEntitySet("Product");
        private static readonly IEdmEntityType TestEntityType = Model.FindDeclaredType("PerformanceServices.Edm.AdventureWorks.Product") as IEdmEntityType;

        #region Full & Lite Json deserializer comparison

        // uncomment it to run
        // [Benchmark]
        // ##ODL Perf of FullDeserializer->LiteDeserializer = 1104165.25 -> 656457 saving 40.55%##
        // ##ODL Averagely Full Ticks = Lite Ticks x 1.68##
        public void TestCompareFullAndLiteODataReaders()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("\r\nFullDeserializer, LiteDeserializer");
            var warnup_afddsfdsfds = RunODataReaderTest(false, MetadataEnablingLevel.Full);
            var warnup_kkgjjgkvc = RunODataReaderTest(false, MetadataEnablingLevel.Lite);
            System.Collections.Generic.List<long> timespanArrayForFull = new System.Collections.Generic.List<long>();
            System.Collections.Generic.List<long> timespanArrayForLite = new System.Collections.Generic.List<long>();
            for (int i = 0; i < 4; i++)
            {
                string timeMaps = "";
                var ret = RunODataReaderTest(false, MetadataEnablingLevel.Full);
                long timespan1 = ret.Item1;
                timespanArrayForFull.Add(timespan1);
                timeMaps += ret.Item2;

                ret = RunODataReaderTest(false, MetadataEnablingLevel.Lite);
                long timespan2 = ret.Item1;
                timespanArrayForLite.Add(timespan2);
                timeMaps += ret.Item2;

                sb.AppendLine(timespan1 + " , " + timespan2 + timeMaps);
            }

            sb.AppendFormat("##ODL Perf of FullDeserializer->LiteDeserializer = {0} -> {1} saving {2}%##\r\n",
                timespanArrayForFull.Average(), timespanArrayForLite.Average(),
                ((timespanArrayForFull.Average() - timespanArrayForLite.Average()) * 100 / timespanArrayForFull.Average()).ToString(".00"));
            sb.AppendLine("##ODL Averagely Full Ticks = Lite Ticks x "
                + (timespanArrayForFull.Average() / timespanArrayForLite.Average()).ToString(".00")
                + "##");
            Assert.True(false, sb.ToString()
                + (System.Diagnostics.Process.GetCurrentProcess().Id + ":" + System.Threading.Thread.CurrentThread.ManagedThreadId)
                + ("@" + DateTime.Now.ToLongTimeString()));

        }
        public Tuple<long, string> RunODataReaderTest(bool useJsonNetReader, MetadataEnablingLevel metadataEnablingLevel)
        {
            //ODataInputContext.DefaultToODataReader = !useJsonNetReader;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            ODataResourceSet feed;
            System.Collections.Generic.List<ODataResource> entryList;
            bool isFullValidation = true;
            string timeMap = ReadFeedTestAndMeasureTmp("EntryIncludeSpatialWithExpansions.json", 100, isFullValidation, out feed, out entryList, metadataEnablingLevel);
            var milli = watch.ElapsedTicks;
            return Tuple.Create(milli, timeMap);
        }

        private class ReaderPerfTimer
        {
            long[] ticksHitCounters = new long[50];
            long[] ticksReadingStartTotalSpan = new long[50];
            long[] ticksReadingEndTotalSpan = new long[50];
            string[] chainNames = new string[50];
            long ticksTmp = 0;
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int indentTmp = 0;
            int resourceCount = 0;

            public bool MarkPreRead()
            {
                ticksTmp = stopwatch.ElapsedTicks;
                return true;
            }

            public void MarkPostRead(ODataReader feedReader)
            {
                {
                    if (feedReader.State == ODataReaderState.ResourceStart)
                    {
                        resourceCount++;
                    }

                    if (feedReader.State.ToString().EndsWith("Start"))
                    {
                        indentTmp += 1;
                        sb.AppendFormat("{0}{1}\r\n", new string(' ', 2 * indentTmp), feedReader.State.ToString());
                        ticksReadingStartTotalSpan[indentTmp] += stopwatch.ElapsedTicks - ticksTmp;
                        ticksHitCounters[indentTmp]++;
                        chainNames[indentTmp] = feedReader.State.ToString();
                    }
                    else if (feedReader.State.ToString().EndsWith("End"))
                    {
                        Assert.Equal(chainNames[indentTmp].Replace("Start", "End"), feedReader.State.ToString());
                        ticksReadingEndTotalSpan[indentTmp] += stopwatch.ElapsedTicks - ticksTmp;
                        sb.AppendFormat("{0}{1}\r\n", new string(' ', 2 * indentTmp), feedReader.State.ToString());
                        indentTmp -= 1;
                    }
                    else
                    {
                        sb.AppendFormat("{0}{1}\r\n", new string(' ', 2 * indentTmp), feedReader.State.ToString());
                    }
                }
            }

            public string GetReadPayloadStructure()
            {
                return sb.ToString();
            }

            public string GetReadPayloadTimeMap(MetadataEnablingLevel metadataEnablingLevel)
            {
                var timeMapCurrent = "";
                for (int k = 0; k < 14; k++)
                {
                    timeMapCurrent += string.Format("[{0}x{1}+End]:{2}+{3}={4}.Ticks, ",
                        ticksHitCounters[k], chainNames[k], ticksReadingStartTotalSpan[k], ticksReadingEndTotalSpan[k],
                        ticksReadingStartTotalSpan[k] + ticksReadingEndTotalSpan[k]);
                }

                while (timeMapCurrent.EndsWith("[0x+End]:0+0=0.Ticks, "))
                    timeMapCurrent = timeMapCurrent.Substring(0, timeMapCurrent.Length - "[0x+End]:0+0=0.Ticks, ".Length);
                if (timeMapCurrent.StartsWith("[0x+End]:0+0=0.Ticks, "))
                    timeMapCurrent = timeMapCurrent.Substring("[0x+End]:0+0=0.Ticks, ".Length, timeMapCurrent.Length - "[0x+End]:0+0=0.Ticks, ".Length);
                timeMapCurrent = metadataEnablingLevel + "(Total:" + stopwatch.ElapsedTicks + ".Ticks) " + timeMapCurrent;
                return timeMapCurrent;
            }
        }

        private string ReadFeedTestAndMeasureTmp(string templateFile, int entryCount, bool isFullValidation, out ODataResourceSet feed, out System.Collections.Generic.List<ODataResource> entryList, MetadataEnablingLevel metadataEnablingLevel)
        {
            var payloads = PayloadGenerator.GenerateFeed(templateFile, entryCount);
            feed = new ODataResourceSet();
            entryList = new System.Collections.Generic.List<ODataResource>();
            string timeMaps = "";
            for (int i = 0; i < 2; i++)
            {
                using (var stream = new MemoryStream(payloads))
                {
                    using (var messageReader = ODataMessageHelper.CreateMessageReader(stream, Model, ODataMessageKind.Response, isFullValidation, metadataEnablingLevel))
                    {
                        ODataReader feedReader = messageReader.CreateODataResourceSetReader(TestEntitySet, TestEntityType);
                        ReaderPerfTimer perfTimer = new ReaderPerfTimer();
                        while (perfTimer.MarkPreRead() && feedReader.Read())
                        {
                            perfTimer.MarkPostRead(feedReader);
                            feed = feed ?? feedReader.Item as ODataResourceSet;
                            if (feedReader.State == ODataReaderState.ResourceEnd)
                            {
                                var entry = feedReader.Item as ODataResource;
                                entryList.Add(entry);
                            }
                        }

                        var stru = perfTimer.GetReadPayloadStructure();
                        Console.WriteLine(stru);
                        timeMaps += "\r\n" + perfTimer.GetReadPayloadTimeMap(metadataEnablingLevel);
                    }
                }
            }

            return timeMaps;
        }
        #endregion

        [Benchmark]
        public void ReadFeed()
        {
            ReadFeedTestAndMeasure("Entry.json", 1000, true);
        }

        [Benchmark]
        public void ReadFeedIncludeSpatial()
        {
            ReadFeedTestAndMeasure("EntryIncludeSpatial.json", 1000, true);
        }

        [Benchmark]
        public void ReadFeedWithExpansions()
        {
            ReadFeedTestAndMeasure("EntryWithExpansions.json", 100, true);
        }

        [Benchmark]
        public void ReadFeedIncludeSpatialWithExpansions()
        {
            ReadFeedTestAndMeasure("EntryIncludeSpatialWithExpansions.json", 100, true);
        }

        [Benchmark]
        public void ReadFeed_NoValidation()
        {
            ReadFeedTestAndMeasure("Entry.json", 1000, false);
        }

        [Benchmark]
        public void ReadFeedIncludeSpatial_NoValidation()
        {
            ReadFeedTestAndMeasure("EntryIncludeSpatial.json", 1000, false);
        }

        [Benchmark]
        public void ReadFeedWithExpansions_NoValidation()
        {
            ReadFeedTestAndMeasure("EntryWithExpansions.json", 100, false);
        }

        [Benchmark]
        public void ReadFeedIncludeSpatialWithExpansions_NoValidation()
        {
            ReadFeedTestAndMeasure("EntryIncludeSpatialWithExpansions.json", 100, false);
        }

        private void ReadFeedTestAndMeasure(string templateFile, int entryCount, bool isFullValidation)
        {
            foreach (var iteration in Benchmark.Iterations)
            {
                using (var stream = new MemoryStream(PayloadGenerator.GenerateFeed(templateFile, entryCount)))
                {
                    using (iteration.StartMeasurement())
                    {
                        using (var messageReader = ODataMessageHelper.CreateMessageReader(stream, Model, ODataMessageKind.Response, isFullValidation))
                        {
                            ODataReader feedReader = messageReader.CreateODataResourceSetReader(TestEntitySet, TestEntityType);
                            while (feedReader.Read()) { }
                        }
                    }
                }
            }
        }
    }
}
