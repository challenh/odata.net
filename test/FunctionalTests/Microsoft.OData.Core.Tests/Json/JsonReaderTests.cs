//---------------------------------------------------------------------
// <copyright file="JsonReaderTests.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------

using System;
using System.IO;
using System.Text;
using FluentAssertions;
using Microsoft.OData.Json;
using Xunit;

namespace Microsoft.OData.Tests.Json
{
    public class JsonReaderTests
    {
        [Fact]
        public void DottedNumberShouldBeReadAsDecimal()
        {
            this.CreateJsonLightReader("42.0").ReadPrimitiveValue().Should().BeOfType<Decimal>();
        }

        [Fact]
        public void NonDottedNumberShouldBeReadAsInt()
        {
            this.CreateJsonLightReader("42").ReadPrimitiveValue().Should().BeOfType<Int32>();
        }

        [Fact]
        public void TrueShouldBeReadAsBoolean()
        {
            this.CreateJsonLightReader("true").ReadPrimitiveValue().Should().BeOfType<Boolean>();
        }

        [Fact]
        public void FalseShouldBeReadAsBoolean()
        {
            this.CreateJsonLightReader("false").ReadPrimitiveValue().Should().BeOfType<Boolean>();
        }

        [Fact]
        public void NullShouldBeReadAsNull()
        {
            this.CreateJsonLightReader("null").ReadPrimitiveValue().Should().BeNull();
        }

        [Fact]
        public void QuotedNumberShouldBeReadAsString()
        {
            this.CreateJsonLightReader("\"42\"").ReadPrimitiveValue().Should().BeOfType<String>();
        }

        [Fact]
        public void QuotedISO8601DateTimeShouldBeReadAsString()
        {
            this.CreateJsonLightReader("\"2012-08-14T19:39Z\"").ReadPrimitiveValue().Should().BeOfType<String>();
        }

        [Fact]
        public void QuotedNullShouldBeReadAsString()
        {
            this.CreateJsonLightReader("\"null\"").ReadPrimitiveValue().Should().BeOfType<String>();
        }

        [Fact]
        public void QuotedBooleanValueShouldBeReadAsString()
        {
            this.CreateJsonLightReader("\"true\"").ReadPrimitiveValue().Should().BeOfType<String>();
        }

        [Fact]
        public void QuotedAspNetDateTimeValueShouldBeReadAsStringInJsonLight()
        {
            this.CreateJsonLightReader("\"\\/Date(628318530718)\\/\"").ReadPrimitiveValue().Should().BeOfType<String>();
        }

        [Fact]
        public void JsonReaderRawValueTest()
        {
            string jsonValue = "{ \"data\" : [123, \"abc\", 456], \"name\": \"hello789\" }";
            JsonReader reader = new JsonReader(new StringReader(jsonValue), isIeee754Compatible: false);
            StringBuilder sb = new StringBuilder();
            reader.Read();
            sb.Append(reader.RawValue.ToString());
            Assert.True(sb.Length > 0);
            reader.Read();
            sb.Append(reader.RawValue.ToString());
            Assert.Equal("{\"data\":", sb.ToString());
            while (reader.Read())
            {
                Assert.True(reader.RawValue.Length > 0);
                sb.Append(reader.RawValue.ToString());
            }

            Assert.Equal("{\"data\":[123,\"abc\",456],\"name\":\"hello789\"}", sb.ToString());
        }

        private JsonReader CreateJsonLightReader(string jsonValue)
        {
            JsonReader reader = new JsonReader(new StringReader(String.Format("{{ \"data\" : {0} }}", jsonValue)), isIeee754Compatible: false);
            reader.Read();
            reader.ReadStartObject();
            reader.ReadPropertyName();
            reader.NodeType.Should().Be(JsonNodeType.PrimitiveValue);

            return reader;
        }
    }
}
