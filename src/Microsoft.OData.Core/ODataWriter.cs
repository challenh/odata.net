//---------------------------------------------------------------------
// <copyright file="ODataWriter.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------

namespace Microsoft.OData
{
    #region Namespaces

    #if PORTABLELIB
    using System.Threading.Tasks;
#endif

    #endregion Namespaces

    /// <summary>
    /// Base class for OData writers.
    /// </summary>
    public abstract class ODataWriter
    {
        /// <summary>Starts the writing of a resource set.</summary>
        /// <param name="resourceSet">The resource set or collection to write.</param>
        public abstract void WriteStart(ODataResourceSet resourceSet);

#if PORTABLELIB
        /// <summary> Asynchronously start writing a resource set. </summary>
        /// <returns>A task instance that represents the asynchronous write operation.</returns>
        /// <param name="resourceSet">The resource set or collection to write.</param>
        public abstract Task WriteStartAsync(ODataResourceSet resourceSet);
#endif

        /// <summary>Starts the writing of a resource.</summary>
        /// <param name="resource">The resource or item to write.</param>
        public abstract void WriteStart(ODataResource resource);

#if PORTABLELIB
        /// <summary> Asynchronously start writing a resource. </summary>
        /// <returns>A task instance that represents the asynchronous write operation.</returns>
        /// <param name="resource">The resource or item to write.</param>
        public abstract Task WriteStartAsync(ODataResource resource);
#endif

        /// <summary>Starts the writing of a nested resource info.</summary>
        /// <param name="nestedResourceInfo">The nested resource info to write.</param>
        public abstract void WriteStart(ODataNestedResourceInfo nestedResourceInfo);

#if PORTABLELIB
        /// <summary> Asynchronously start writing a nested resource info. </summary>
        /// <returns>A task instance that represents the asynchronous write operation.</returns>
        /// <param name="nestedResourceInfo">The nested resource info to writer.</param>
        public abstract Task WriteStartAsync(ODataNestedResourceInfo nestedResourceInfo);
#endif

        /// <summary>Finishes the writing of a resource set, a resource, or a nested resource info.</summary>
        public abstract void WriteEnd();

#if PORTABLELIB
        /// <summary> Asynchronously finish writing a resource set, resource, or nested resource info. </summary>
        /// <returns>A task instance that represents the asynchronous write operation.</returns>
        public abstract Task WriteEndAsync();
#endif

        /// <summary> Writes an entity reference link, which is used to represent binding to an existing resource in a request payload. </summary>
        /// <param name="entityReferenceLink">The entity reference link to write.</param>
        /// <remarks>
        /// This method can only be called for writing request messages. The entity reference link must be surrounded
        /// by a nested resource info written through WriteStart/WriteEnd.
        /// The <see cref="ODataNestedResourceInfo.Url"/> will be ignored in that case and the Uri from the <see cref="ODataEntityReferenceLink.Url"/> will be used
        /// as the binding URL to be written.
        /// </remarks>
        public abstract void WriteEntityReferenceLink(ODataEntityReferenceLink entityReferenceLink);

#if PORTABLELIB
        /// <summary> Asynchronously writes an entity reference link, which is used to represent binding to an existing resource in a request payload. </summary>
        /// <returns>A task instance that represents the asynchronous write operation.</returns>
        /// <param name="entityReferenceLink">The entity reference link to write.</param>
        /// <remarks>
        /// This method can only be called for writing request messages. The entity reference link must be surrounded
        /// by a nested resource info written through WriteStart/WriteEnd.
        /// The <see cref="ODataNestedResourceInfo.Url"/> will be ignored in that case and the Uri from the <see cref="ODataEntityReferenceLink.Url"/> will be used
        /// as the binding URL to be written.
        /// </remarks>
        public abstract Task WriteEntityReferenceLinkAsync(ODataEntityReferenceLink entityReferenceLink);
#endif

        /// <summary>Flushes the write buffer to the underlying stream.</summary>
        public abstract void Flush();

#if PORTABLELIB
        /// <summary>Flushes the write buffer to the underlying stream asynchronously.</summary>
        /// <returns>A task instance that represents the asynchronous operation.</returns>
        public abstract Task FlushAsync();
#endif
    }
}
