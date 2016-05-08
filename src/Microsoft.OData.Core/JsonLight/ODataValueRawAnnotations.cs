//---------------------------------------------------------------------
// <copyright file="ODataValueRawAnnotations.cs" company="Microsoft">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//---------------------------------------------------------------------

namespace Microsoft.OData
{
    #region Namespaces
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    #endregion Namespaces

    /// <summary>
    /// Class representing a property value's raw annotations.
    /// </summary>
    public sealed class ODataValueRawAnnotations
    {
        /// <summary>The (instance and property) annotations included in this annotation group.</summary>
        private IDictionary<string, ODataUntypedValue> annotations;

        /// <summary>
        /// The raw annotation names and values.
        /// </summary>
        /// <remarks>The keys in the dictionary are the names of the annotations, the values are their values.</remarks>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "We allow setting of all properties on public ODataLib OM classes.")]
        public IDictionary<string, ODataUntypedValue> Annotations
        {
            get
            {
                return this.annotations;
            }

            set
            {
                this.annotations = value;
            }
        }
    }
}
