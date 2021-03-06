﻿//---------------------------------------------------------------------
// <copyright file="PathBuilderCreationTests.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.OData.Tests.UriParser;
using Microsoft.OData.UriParser;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Vocabularies;
using Xunit;

namespace Microsoft.OData.Tests.ScenarioTests.UriBuilder
{
    public class PathBuilderCreationTests : UriBuilderTestBase
    {
        [Fact]
        public void BuildPathWithFunctionImport()
        {
            ODataUri odataUri = new ODataUri();
            odataUri.ServiceRoot = new Uri("http://gobbledygook/");
            IEdmOperationImport functionImport = HardCodedTestModel.TestModel.EntityContainer.FindOperationImports("GetPet1").Single();
            IEdmEntitySetReferenceExpression reference = functionImport.EntitySet as IEdmEntitySetReferenceExpression;
            OperationSegmentParameter[] parameters = new OperationSegmentParameter[] { new OperationSegmentParameter("id", new ConstantNode(1, "1")) };
            odataUri.Path = new ODataPath(new OperationImportSegment(
                new IEdmOperationImport[] { functionImport },
                reference.ReferencedEntitySet,
                parameters));
            Uri actual = odataUri.BuildUri(ODataUrlConventions.Default);
            Assert.Equal(new Uri("http://gobbledygook/GetPet1(id=1)"), actual);
        }
    }
}
