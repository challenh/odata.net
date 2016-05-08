//---------------------------------------------------------------------
// <copyright file="ODataUntypedPrimitiveValue.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------

using Microsoft.OData.JsonLight;

namespace Microsoft.OData
{
    /// <summary>
    /// OData representation of an untyped primitive value.
    /// </summary>
    public sealed class ODataUntypedPrimitiveValue : ODataUntypedValue
    {
        /// <summary>
        /// The primitive value.
        /// </summary>
        public object Value { get; set; }
    }
}
