//---------------------------------------------------------------------
// <copyright file="ReorderingJsonReaderTests.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------

using System.IO;
using System.Text;
using FluentAssertions;
using Microsoft.OData.Json;
using Microsoft.OData.JsonLight;
using Xunit;

namespace Microsoft.OData.Tests.JsonLight
{
    public class ReorderingJsonReaderTests
    {
        [Fact]
        public void TypeShouldBeMovedToTop()
        {
            var json = @"
            { 
                ""@odata.editlink"":""RelativeUrl"",
                ""@foo.bla"": 4,
                ""@odata.type"": ""SomeEntityType""
            }";

            var reader = CreateReorderingReaderPositionedOnFirstProperty(json);
            reader.ReadPropertyName().Should().Be("@odata.type");
            reader.ReadPrimitiveValue().Should().Be("SomeEntityType");
        }

        [Fact]
        public void ETagShouldBeMovedToTop()
        {
            var json = @"
            { 
                ""@odata.editlink"":""RelativeUrl"",
                ""@foo.bla"": 4,
                ""@odata.etag"": ""etag-val""
            }";

            var reader = CreateReorderingReaderPositionedOnFirstProperty(json);
            reader.ReadPropertyName().Should().Be("@odata.etag");
            reader.ReadPrimitiveValue().Should().Be("etag-val");
        }

        [Fact]
        public void IdShouldBeMovedToTop()
        {
            var json = @"
            { 
                ""@odata.editlink"":""RelativeUrl"",
                ""@foo.bla"": 4,
                ""@odata.id"": 42
            }";

            var reader = CreateReorderingReaderPositionedOnFirstProperty(json);
            reader.ReadPropertyName().Should().Be("@odata.id");
            reader.ReadPrimitiveValue().Should().Be(42);
        }

        [Fact]
        public void CorrectOrderShouldBeTypeThenIdThenEtag()
        {
            const string json = @"
            { 
                ""@odata.editlink"":""RelativeUrl"",
                ""@foo.bla"": 4,
                ""data"": 3.1,
                ""@odata.etag"": ""etag-val"",
                ""@odata.type"": ""SomeEntityType"",
                ""@odata.id"": 42
            }";

            var reader = CreateReorderingReaderPositionedOnFirstProperty(json);

            // Expect type name first.
            reader.ReadPropertyName().Should().Be("@odata.type");
            reader.ReadPrimitiveValue().Should().Be("SomeEntityType");

            // Per the protocol, odata.id and odata.etag can be in either order relative to each other,
            // but we'll (arbitarily) lock down "id" before "etag" for our reordering reader.
            reader.ReadPropertyName().Should().Be("@odata.id");
            reader.ReadPrimitiveValue().Should().Be(42);
            reader.ReadPropertyName().Should().Be("@odata.etag");
            reader.ReadPrimitiveValue().Should().Be("etag-val");
        }

        [Fact]
        public void CorrectOrderShouldBeTypeThenIdThenEtag_RawValue()
        {
            const string json = @"
            { 
                ""@odata.editlink"":""RelativeUrl"",
                ""@foo.bla"": 4,
                ""data"": 3.1,
                ""@odata.etag"": ""etag\\b\\r\\n\\f\\t\"" - val"",
                ""@odata.type"": ""\b\r\n\f\tSomeEntity\\u00D2\u00D3Type"",
                ""@odata.id"": 42
            }";

            var reader = CreateReorderingReaderPositionedOnFirstProperty(json);
            StringBuilder sb = new StringBuilder();
            // Expect type name first.
            reader.GetPropertyName().Should().Be("@odata.type");
            sb.Append(reader.RawValue.ToString());
            reader.Read();
            sb.Append(reader.RawValue.ToString());
            reader.Value.Should().Be("\b\r\n\f\tSomeEntity\\u00D2\u00D3Type");
            reader.Read();

            // Per the protocol, odata.id and odata.etag can be in either order relative to each other,
            // but we'll (arbitarily) lock down "id" before "etag" for our reordering reader.
            reader.GetPropertyName().Should().Be("@odata.id");
            sb.Append(reader.RawValue.ToString());
            reader.Read();
            reader.Value.Should().Be(42);
            sb.Append(reader.RawValue.ToString());
            reader.Read();

            reader.GetPropertyName().Should().Be("@odata.etag");
            sb.Append(reader.RawValue.ToString());
            reader.Read();
            reader.Value.Should().Be("etag\\b\\r\\n\\f\\t\" - val");
            sb.Append(reader.RawValue.ToString());

            Assert.Equal(
                ",\"@odata.type\":\"\\b\\r\\n\\f\\tSomeEntity\\\\u00D2\\u00D3Type\",\"@odata.id\":42,\"@odata.etag\":\"etag\\\\b\\\\r\\\\n\\\\f\\\\t\\\" - val\"",
                sb.ToString());
        }

        /// <summary>
        /// Creates a new <see cref="ReorderingJsonReader"/> and advances it to the first property node
        /// </summary>
        /// <param name="json">The json string to potentially reorder and read.</param>
        /// <returns>The created json reader.</returns>
        private static ReorderingJsonReader CreateReorderingReaderPositionedOnFirstProperty(string json)
        {
            var stringReader = new StringReader(json);
            var innerReader = new JsonReader(stringReader, isIeee754Compatible: true);
            var reader = new ReorderingJsonReader(innerReader, maxInnerErrorDepth: 0);

            reader.NodeType.Should().Be(JsonNodeType.None);
            reader.Read();
            reader.NodeType.Should().Be(JsonNodeType.StartObject);
            reader.Read();
            reader.NodeType.Should().Be(JsonNodeType.Property);
            return reader;
        }
    }
}
