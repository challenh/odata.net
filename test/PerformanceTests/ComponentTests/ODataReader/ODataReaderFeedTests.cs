//---------------------------------------------------------------------
// <copyright file="ODataReaderFeedTests.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------

namespace Microsoft.OData.Performance
{
    using System;
    using System.IO;
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
        public void TestCompareFullAndLiteODataReaders()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("\r\nFullDeserializer, LiteDeserializer");
            for (int i = 0; i < 28; i++)
            {
                long timespan1 = RunODataReaderTest(false, MetadataValidationLevel.Full);
                long timespan2 = RunODataReaderTest(false, MetadataValidationLevel.Lite);
                sb.AppendLine(timespan1 + " , " + timespan2);
            }
            Assert.True(false, sb.ToString()
                + (System.Diagnostics.Process.GetCurrentProcess().Id + ":" + System.Threading.Thread.CurrentThread.ManagedThreadId)
                + ("@" + DateTime.Now.ToLongTimeString()));

        }
        public long RunODataReaderTest(bool useJsonNetReader, MetadataValidationLevel metadataValidationLevel)
        {
            //ODataInputContext.DefaultToODataReader = !useJsonNetReader;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            ODataResourceSet feed;
            System.Collections.Generic.List<ODataResource> entryList;
            ReadFeedTestAndMeasureTmp("EntryIncludeSpatialWithExpansions.json", 100, true, out feed, out entryList, metadataValidationLevel);
            var milli = watch.ElapsedMilliseconds;
            return milli;
        }

        private void ReadFeedTestAndMeasureTmp(string templateFile, int entryCount, bool isFullValidation, out ODataResourceSet feed, out System.Collections.Generic.List<ODataResource> entryList, MetadataValidationLevel metadataValidationLevel)
        {
            var payloads = PayloadGenerator.GenerateFeed(templateFile, entryCount);
            feed = new ODataResourceSet();
            entryList = new System.Collections.Generic.List<ODataResource>();
            for (int i = 0; i < 2; i++)
            {
                using (var stream = new MemoryStream(payloads))
                {
                    using (var messageReader = ODataMessageHelper.CreateMessageReader(stream, Model, ODataMessageKind.Response, isFullValidation, metadataValidationLevel))
                    {
                        ODataReader feedReader = messageReader.CreateODataResourceSetReader(TestEntitySet, TestEntityType);
                        //StringBuilder sb = new StringBuilder();
                        //int indentTmp = 0;
                        //int resourceCount = 0;
                        while (feedReader.Read())
                        {
                            //{
                            //    if (feedReader.State == ODataReaderState.ResourceSetStart)
                            //    {
                            //        resourceCount++;
                            //    }

                            //    if (feedReader.State == ODataReaderState.NestedResourceInfoEnd)
                            //    {
                            //    }
                            //    else if (feedReader.State == ODataReaderState.NestedResourceInfoStart)
                            //    {
                            //    }
                            //    else
                            //    if (feedReader.State.ToString().EndsWith("Start"))
                            //    {
                            //        indentTmp += 2;
                            //        sb.AppendFormat("{0}{1}\r\n", new string(' ', indentTmp), feedReader.State.ToString());
                            //    }
                            //    else if (feedReader.State.ToString().EndsWith("End"))
                            //    {
                            //        sb.AppendFormat("{0}{1}\r\n", new string(' ', indentTmp), feedReader.State.ToString());
                            //        indentTmp -= 2;
                            //    }
                            //    else
                            //    {
                            //        sb.AppendFormat("{0}{1}\r\n", new string(' ', indentTmp), feedReader.State.ToString());
                            //    }
                            //}
                            feed = feed ?? feedReader.Item as ODataResourceSet;
                            if (feedReader.State == ODataReaderState.ResourceEnd)
                            {
                                var entry = feedReader.Item as ODataResource;
                                entryList.Add(entry);
                            }
                        }

                        //Console.Write(payloads.Length + " bytes. \r\n"
                        //    + resourceCount + " resources.\r\n"
                        //    + sb.ToString());
                    }
                }
            }
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
