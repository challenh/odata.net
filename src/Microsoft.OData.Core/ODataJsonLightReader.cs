//---------------------------------------------------------------------
// <copyright file="ODataJsonLiteReader.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------

namespace Microsoft.OData
{
    #region Namespaces
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
#if PORTABLELIB
    using System.Threading.Tasks;
#endif
    using Microsoft.OData.Edm;
    using Microsoft.OData.Evaluation;
    using Microsoft.OData.JsonLight;
    using Microsoft.OData.Metadata;
    using Json;
    using Spatial;
    using ODataErrorStrings = Microsoft.OData.Strings;
    #endregion Namespaces

    /// <summary>
    /// A streaming reader producing OData confirming results. 
    /// </summary>
    internal class ODataJsonLightReader : ODataReader
    {
        /// <summary>The input to read the payload from.</summary>
        private readonly ODataJsonLightInputContext inputContext;

        private readonly IJsonReader jsonReader;

        /// <summary>true if the reader is created for reading a resourceSet; false when it is created for reading an entry.</summary>
        private readonly bool readingResourceSet;

        /// <summary>true if the reader is created for reading expanded navigation property in delta response; false otherwise.</summary>
        private readonly bool readingDelta;

        /// <summary>Stack of reader scopes to keep track of the current context of the reader.</summary>
        private readonly Stack<Scope> scopes = new Stack<Scope>();

        private readonly Queue<Scope> nextScopes = new Queue<Scope>();

        /// <summary>If not null, the reader will notify the implementer of the interface of relevant state changes in the reader.</summary>
        private readonly IODataReaderWriterListener listener;

        /// <summary>The number of entries which have been started but not yet ended.</summary>
        private int currentResourceDepth;

        #region variables during reading
        private ODataJsonLightContextUriParseResult contextUriParseResult;
        #endregion

        internal ODataJsonLightReader(
            ODataJsonLightInputContext jsonLightInputContext,
            IEdmNavigationSource navigationSource,
            IEdmEntityType expectedEntityType,
            bool readingResourceSet,
            bool readingParameter = false,
            bool readingDelta = false,
            IODataReaderWriterListener listener = null)
        {
            Debug.Assert(jsonLightInputContext != null, "jsonLightInputContext != null");

            this.inputContext = jsonLightInputContext;
            this.readingResourceSet = readingResourceSet;
            this.readingDelta = readingDelta;
            this.listener = listener;
            this.currentResourceDepth = 0;
            this.jsonReader = jsonLightInputContext.JsonReader;

            this.EnterScope(
                new Scope(ODataReaderState.Start, null, navigationSource, expectedEntityType, new ODataUri()));
        }

        /// <summary>
        /// The current state of the reader.
        /// </summary>
        public override sealed ODataReaderState State
        {
            get
            {
                this.inputContext.VerifyNotDisposed();
                Debug.Assert(this.scopes != null && this.scopes.Count > 0, "A scope must always exist.");
                return this.scopes.Peek().State;
            }
        }

        /// <summary>
        /// The most recent <see cref="ODataItem"/> that has been read.
        /// </summary>
        public override sealed ODataItem Item
        {
            get
            {
                this.inputContext.VerifyNotDisposed();
                Debug.Assert(this.scopes != null && this.scopes.Count > 0, "A scope must always exist.");
                return this.scopes.Peek().Item;
            }
        }

        /// <summary>
        /// Returns the current item as <see cref="ODataResource"/>. Must only be called if the item actually is an entry.
        /// </summary>
        protected ODataResource CurrentResource
        {
            get
            {
                Debug.Assert(this.Item == null || this.Item is ODataResource, "this.Item is ODataResource");
                return (ODataResource)this.Item;
            }
        }

        /// <summary>
        /// Returns the current item as <see cref="ODataResourceSet"/>. Must only be called if the item actually is a resourceSet.
        /// </summary>
        protected ODataResourceSet CurrentResourceSet
        {
            get
            {
                Debug.Assert(this.Item is ODataResourceSet, "this.Item is ODataResourceSet");
                return (ODataResourceSet)this.Item;
            }
        }

        /// <summary>
        /// Returns the current resource depth.
        /// </summary>
        protected int CurrentResourceDepth
        {
            get
            {
                return this.currentResourceDepth;
            }
        }

        /// <summary>
        /// Returns the current item as <see cref="ODataNestedResourceInfo"/>. Must only be called if the item actually is a navigation link.
        /// </summary>
        protected ODataNestedResourceInfo CurrentNavigationLink
        {
            get
            {
                Debug.Assert(this.Item is ODataNestedResourceInfo, "this.Item is ODataNestedResourceInfo");
                return (ODataNestedResourceInfo)this.Item;
            }
        }

        /// <summary>
        /// Returns the current item as <see cref="ODataEntityReferenceLink"/>. Must only be called if the item actually is an entity reference link.
        /// </summary>
        protected ODataEntityReferenceLink CurrentEntityReferenceLink
        {
            get
            {
                Debug.Assert(this.Item is ODataEntityReferenceLink, "this.Item is ODataEntityReferenceLink");
                return (ODataEntityReferenceLink)this.Item;
            }
        }

        /// <summary>
        /// Returns the expected entity type for the current scope.
        /// </summary>
        protected IEdmStructuredType CurrentEntityType
        {
            get
            {
                Debug.Assert(this.scopes != null && this.scopes.Count > 0, "A scope must always exist.");
                IEdmStructuredType entityType = this.scopes.Peek().EntityType as IEdmStructuredType;
                Debug.Assert(entityType == null || this.inputContext.Model.IsUserModel(), "We can only have entity type if we also have metadata.");
                return entityType;
            }

            set
            {
                this.scopes.Peek().EntityType = value;
            }
        }

        /// <summary>
        /// Returns the navigation source for the current scope.
        /// </summary>
        protected IEdmNavigationSource CurrentNavigationSource
        {
            get
            {
                Debug.Assert(this.scopes != null && this.scopes.Count > 0, "A scope must always exist.");
                IEdmNavigationSource navigationSource = this.scopes.Peek().NavigationSource;
                Debug.Assert(navigationSource == null || this.inputContext.Model.IsUserModel(), "We can only have navigation source if we also have metadata.");
                return navigationSource;
            }
        }

        /// <summary>
        /// Returns the current scope.
        /// </summary>
        protected Scope CurrentScope
        {
            get
            {
                Debug.Assert(this.scopes != null && this.scopes.Count > 0, "A scope must always exist.");
                return this.scopes.Peek();
            }
        }

        /// <summary>
        /// Returns the scope of the entity owning the current link.
        /// </summary>
        protected Scope LinkParentEntityScope
        {
            get
            {
                Debug.Assert(this.scopes != null && this.scopes.Count > 1, "We must have at least two scoped for LinkParentEntityScope to be called.");
                Debug.Assert(this.scopes.Peek().State == ODataReaderState.NestedResourceInfoStart, "The LinkParentEntityScope can only be accessed when in NavigationLinkStart state.");
                return this.scopes.Skip(1).First();
            }
        }

        /// <summary>
        /// A flag indicating whether the reader is at the top level.
        /// </summary>
        protected bool IsTopLevel
        {
            get
            {
                Debug.Assert(this.scopes != null, "Scopes must exist.");

                // there is the root scope at the top (when the writer has not started or has completed) 
                // and then the top-level scope (the top-level resource/resourceSet item) as the second scope on the stack
                return this.scopes.Count <= 2;
            }
        }

        /// <summary>
        /// If the current scope is a content of an expanded link, this returns the parent navigation link scope, otherwise null.
        /// </summary>
        protected Scope ExpandedLinkContentParentScope
        {
            get
            {
                Debug.Assert(this.scopes != null, "this.scopes != null");
                if (this.scopes.Count > 1)
                {
                    Scope parentScope = this.scopes.Skip(1).First();
                    if (parentScope.State == ODataReaderState.NestedResourceInfoStart)
                    {
                        return parentScope;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// True if we are reading an entry or resourceSet that is the direct content of an expanded link. Otherwise false.
        /// </summary>
        protected bool IsExpandedLinkContent
        {
            get
            {
                return this.ExpandedLinkContentParentScope != null;
            }
        }

        /// <summary>
        /// Set to true if a resourceSet is being read.
        /// </summary>
        protected bool ReadingResourceSet
        {
            get
            {
                return this.readingResourceSet;
            }
        }

        /// <summary>
        /// Returns true if we are reading a nested payload,
        /// e.g. an expanded entry or resourceSet within a delta payload, 
        /// or an entry or a resourceSet within a parameters payload.
        /// </summary>
        protected bool IsReadingNestedPayload
        {
            get
            {
                return this.readingDelta || this.listener != null;
            }
        }

        /// <summary>
        /// Reads the next <see cref="ODataItem"/> from the message payload.
        /// </summary>
        /// <returns>true if more items were read; otherwise false.</returns>
        public override sealed bool Read()
        {
            this.VerifyCanRead(true);
            return this.InterceptException(this.ReadSynchronously);
        }

#if PORTABLELIB
        /// <summary>
        /// Asynchronously reads the next <see cref="ODataItem"/> from the message payload.
        /// </summary>
        /// <returns>A task that when completed indicates whether more items were read.</returns>
        [SuppressMessage("Microsoft.MSInternal", "CA908:AvoidTypesThatRequireJitCompilationInPrecompiledAssemblies", Justification = "API design calls for a bool being returned from the task here.")]
        public override sealed Task<bool> ReadAsync()
        {
            this.VerifyCanRead(false);
            return this.ReadAsynchronously().FollowOnFaultWith(t => this.EnterScope(new Scope(ODataReaderState.Exception, null, null, null, null)));
        }

        /// <summary>
        /// Asynchronously reads the next <see cref="ODataItem"/> from the message payload.
        /// </summary>
        /// <returns>A task that when completed indicates whether more items were read.</returns>
        [SuppressMessage("Microsoft.MSInternal", "CA908:AvoidTypesThatRequireJitCompilationInPrecompiledAssemblies", Justification = "API design calls for a bool being returned from the task here.")]
        protected virtual Task<bool> ReadAsynchronously()
        {
            // We are reading from the fully buffered read stream here; thus it is ok
            // to use synchronous reads and then return a completed task
            // NOTE: once we switch to fully async reading this will have to change
            return TaskUtils.GetTaskForSynchronousOperation<bool>(this.ReadSynchronously);
        }
#endif

        /// <summary>
        /// Pushes the <paramref name="scope"/> on the stack of scopes.
        /// </summary>
        /// <param name="scope">The scope to enter.</param>
        protected void EnterScope(Scope scope)
        {
            Debug.Assert(scope != null, "scope != null");

            // TODO: implement some basic validation that the transitions are ok
            this.scopes.Push(scope);
            if (this.listener != null)
            {
                if (scope.State == ODataReaderState.Exception)
                {
                    this.listener.OnException();
                }
                else if (scope.State == ODataReaderState.Completed)
                {
                    this.listener.OnCompleted();
                }
            }
        }

        /// <summary>
        /// Replaces the current scope's state with the specified <paramref name="scope"/>.
        /// </summary>
        /// <param name="fromState">The scope state to replace the current scope state with.</param>
        /// <param name="scope">The scope state to replace the current scope state with.</param>
        protected void ReplaceScope(ODataReaderState fromState, Scope scope)
        {
            Debug.Assert(this.scopes.Count > 0, "Stack must always be non-empty.");
            Debug.Assert(scope != null, "scope != null");
            Debug.Assert(scope.State != ODataReaderState.ResourceEnd, "Call EndResource instead.");

            Scope popped = this.scopes.Pop();
            Debug.Assert(popped.State == fromState, "popped.State == fromState");
            this.EnterScope(scope);
        }

        protected void ReplaceScope(ODataReaderState fromState, ODataReaderState withState)
        {
            Debug.Assert(this.scopes.Count > 0, "Stack must always be non-empty.");
            Scope tmpScope = this.scopes.Peek();
            if (tmpScope.State != fromState)
            {
                throw new InvalidCastException();
            }

            tmpScope.State = withState;
        }

        /// <summary>
        /// Removes the current scope from the stack of all scopes.
        /// </summary>
        /// <param name="state">The expected state of the current scope (to be popped).</param>
        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "state", Justification = "Used in debug builds in assertions.")]
        [SuppressMessage("Microsoft.Performance", "CA1804:RemoveUnusedLocals", MessageId = "scope", Justification = "Used in debug builds in assertions.")]
        protected void PopScope(ODataReaderState state)
        {
            Debug.Assert(this.scopes.Count > 1, "Stack must have more than 1 items in order to pop an item.");

            Scope scope = this.scopes.Pop();
            Debug.Assert(scope.State == state, "scope.State == state");
        }

        /// <summary>
        /// Called to transition into the ResourceEnd state.
        /// </summary>
        protected void EndCurrentResource()
        {
            Scope endScope = new Scope(ODataReaderState.ResourceEnd, this.CurrentResource, this.CurrentNavigationSource, this.CurrentEntityType, new ODataUri());
            this.PopScope(ODataReaderState.ResourceStart);
            this.EnterScope(endScope);
        }

        /// <summary>
        /// If an entity type name is found in the payload this method is called to apply it to the current scope.
        /// This method should be called even if the type name was not found in which case a null should be passed in.
        /// The method validates that some type will be available as the current entity type after it returns (if we are parsing using metadata).
        /// </summary>
        /// <param name="entityTypeNameFromPayload">The entity type name found in the payload or null if no type was specified in the payload.</param>
        protected void ApplyEntityTypeNameFromPayload(string entityTypeNameFromPayload)
        {
            Debug.Assert(
                this.scopes.Count > 0 && this.scopes.Peek().Item is ODataResource,
                "Entity type can be applied only when in entry scope.");

            SerializationTypeNameAnnotation serializationTypeNameAnnotation;
            EdmTypeKind targetTypeKind;
            IEdmEntityTypeReference targetEntityTypeReference =
                (IEdmEntityTypeReference)ReaderValidationUtils.ResolvePayloadTypeNameAndComputeTargetType(
                    EdmTypeKind.Entity,
                /*defaultPrimitivePayloadType*/ null,
                    this.CurrentEntityType.ToTypeReference(),
                    entityTypeNameFromPayload,
                    this.inputContext.Model,
                    this.inputContext.MessageReaderSettings,
                    () => EdmTypeKind.Entity,
                    out targetTypeKind,
                    out serializationTypeNameAnnotation);

            IEdmEntityType targetEntityType = null;
            ODataResource entry = this.CurrentResource;
            if (targetEntityTypeReference != null)
            {
                targetEntityType = targetEntityTypeReference.EntityDefinition();
                entry.TypeName = targetEntityType.FullTypeName();

                if (serializationTypeNameAnnotation != null)
                {
                    entry.SetAnnotation(serializationTypeNameAnnotation);
                }
            }
            else if (entityTypeNameFromPayload != null)
            {
                entry.TypeName = entityTypeNameFromPayload;
            }

            // Set the current entity type since the type from payload might be more derived than
            // the expected one.
            this.CurrentEntityType = targetEntityType;
        }

        /// <summary>
        /// Reads the next <see cref="ODataItem"/> from the message payload.
        /// </summary>
        /// <returns>true if more items were read; otherwise false.</returns>
        protected bool ReadSynchronously()
        {
            if (nextScopes.Count > 0)
            {
                Debug.Assert(this.State == ODataReaderState.NestedResourceInfoStart || this.State == ODataReaderState.ResourceSetStart,
                    "this.State == ODataReaderState.NavigationLinkStart || this.State == ODataReaderState.ResourceSetStart");
                Scope scope = nextScopes.Dequeue();
                this.EnterScope(scope);
                return true;
            }

            bool result = false;
            switch (this.State)
            {
                case ODataReaderState.Start:
                    this.jsonReader.Read();
                    if (this.readingResourceSet)
                    {
                        this.PreRootResourceSetStart(); // will set to ODataReaderState.ResourceSetStart
                    }
                    else
                    {
                        this.EnterScope(new ResourceScope(this.CurrentNavigationSource, this.CurrentEntityType, new ODataUri()));
                    }

                    result = true;
                    break;

                case ODataReaderState.NestedResourceInfoStart:
                    result = this.ReadAtNavigationLinkStartImplementationSynchronously();
                    break;

                case ODataReaderState.NestedResourceInfoEnd:
                    result = this.ReadAtNavigationLinkEndImplementationSynchronously();
                    break;

                case ODataReaderState.ResourceSetStart:
                    result = this.ReadAtResourceSetStartImplementationSynchronously();
                    break;

                case ODataReaderState.ResourceSetEnd:
                    result = this.ReadAtResourceSetEndImplementationSynchronously();
                    break;

                case ODataReaderState.ResourceStart:
                    this.IncreaseResourceDepth();
                    result = ContiueReadingResource(true);
                    break;

                case ODataReaderState.ResourceEnd:
                    result = this.ReadAtResourceEndImplementationSynchronously();
                    this.DecreaseResourceDepth();
                    break;

                case ODataReaderState.Exception:    // fall through
                case ODataReaderState.Completed:
                    throw new ODataException(Strings.ODataReaderCore_NoReadCallsAllowed(this.State));

                default:
                    Debug.Assert(false, "Unsupported reader state " + this.State + " detected.");
                    throw new ODataException(Strings.General_InternalError(InternalErrorCodes.ODataReaderCore_ReadImplementation));
            }

            return result;
        }

        protected void ApplyResourceSetStartingAnnotationsTo(ODataResourceSet resourceSet)
        {
            NestedResourceInfoScope navigationScope = this.scopes.Peek() as NestedResourceInfoScope;
            if (navigationScope != null)
            {
                // this.scopes should have  ...-ResourceScope(annotations)-NestedResourceInfoScope(property name)
                ODataNestedResourceInfo nestedInfo = (ODataNestedResourceInfo)navigationScope.Item;
                string propertyName = nestedInfo.Name + "@";
                ODataResource parentResource = (ODataResource)this.LinkParentEntityScope.Item;
                Dictionary<string, ODataUntypedValue> resourceAnnotations = parentResource.GetAnnotation<Dictionary<string, ODataUntypedValue>>();
                foreach (var tmp in resourceAnnotations)
                {
                    if (tmp.Key.StartsWith(propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        Uri metaDocUrl = this.contextUriParseResult == null ? null : this.contextUriParseResult.MetadataDocumentUri;
                        string annotationName = tmp.Key.Substring(propertyName.Length);
                        ODataJsonLiteReaderUtils.ApplyFeedInstanceAnnotation(
                            this.inputContext, ref this.contextUriParseResult, this.CurrentScope, resourceSet, nestedInfo.Name, annotationName, tmp.Value);
                    }
                }
            }
        }

        /// <summary>
        /// Applies Resource instance annotation.
        /// </summary>
        /// <param name="inputContext">The ODataInputContext.</param>
        /// <param name="resourceScope">The ResourceScope.</param>
        /// <param name="resource">The ODataResource.</param>
        /// <param name="annotationName">The name.</param>
        /// <param name="annotationValue">The value.</param>
        [SuppressMessage("Microsoft.Performance", "CA1800:DoNotCastUnnecessarily", Justification = "The casts aren't actually being done multiple times, since they occur in different cases of the switch statement.")]
        protected void ApplyEntryInstanceAnnotation(
            ODataInputContext inputContext,
            ResourceScope resourceScope,
            ODataResource resource,
            string annotationName,
            ODataUntypedValue annotationValue)
        {
            ODataMessageReaderSettings readerSettings = inputContext.MessageReaderSettings;
            Debug.Assert(resource != null, "resource != null");
            Debug.Assert(!string.IsNullOrEmpty(annotationName), "!string.IsNullOrEmpty(annotationName)");

            ODataStreamReferenceValue mediaResource = resource.MediaResource;
            if (annotationName[0] == '@')
            {
                annotationName = annotationName.Substring(1);
            }

            string tmpValue = ((ODataUntypedPrimitiveValue)annotationValue).Value as string;
            switch (annotationName)
            {
                case ODataAnnotationNames.ODataType:   // 'odata.type'
                    {
                        resource.TypeName = ReaderUtils.AddEdmPrefixOfTypeName(ReaderUtils.RemovePrefixOfTypeName(tmpValue));
                        resourceScope.AnnotatedComplexEntityType = (IEdmEntityType)inputContext.Model.FindDeclaredType(resource.TypeName);
                    }

                    break;
                case ODataAnnotationNames.ODataId:   // 'odata.id'
                    {
                        if (annotationValue == null)
                        {
                            resource.IsTransient = true;
                        }
                        else
                        {
                            resource.Id = new Uri(tmpValue);
                        }
                    }

                    break;
                case ODataAnnotationNames.ODataETag:   // 'odata.etag'
                    {
                        resource.ETag = tmpValue;
                    }

                    break;
                case ODataAnnotationNames.ODataEditLink:    // 'odata.editLink'
                    {
                        resource.EditLink = new Uri(tmpValue);
                    }

                    break;
                case ODataAnnotationNames.ODataReadLink:    // 'odata.readLink'
                    {
                        resource.ReadLink = new Uri(tmpValue);
                    }

                    break;
                case ODataAnnotationNames.ODataMediaEditLink:   // 'odata.mediaEditLink'
                    {
                        ODataJsonLightReaderUtils.EnsureInstance(ref mediaResource);
                        mediaResource.EditLink = new Uri(tmpValue);
                    }

                    break;
                case ODataAnnotationNames.ODataMediaReadLink:   // 'odata.mediaReadLink'
                    {
                        ODataJsonLightReaderUtils.EnsureInstance(ref mediaResource);
                        mediaResource.ReadLink = new Uri(tmpValue);
                    }

                    break;
                case ODataAnnotationNames.ODataMediaContentType:  // 'odata.mediaContentType'
                    {
                        ODataJsonLightReaderUtils.EnsureInstance(ref mediaResource);
                        mediaResource.ContentType = tmpValue;
                    }

                    break;
                case ODataAnnotationNames.ODataMediaETag:  // 'odata.mediaEtag'
                    {
                        ODataJsonLightReaderUtils.EnsureInstance(ref mediaResource);
                        mediaResource.ETag = tmpValue;
                    }

                    break;
                case ODataAnnotationNames.ODataContext:  // 'odata.context' 
                    {
                        ODataJsonLiteReaderUtils.SetContextUri(ref contextUriParseResult, false, tmpValue,
                            inputContext.Model, inputContext.MessageReaderSettings, resourceScope);
                    }

                    break;
                default:
                    break;
            }

            if (mediaResource != null && resource.MediaResource == null)
            {
                // this.SetEntryMediaResource(resourceState, mediaResource);
            }
        }

        /// <summary>
        /// Increments the nested entry count by one and fails if the new value exceeds the maxiumum nested entry depth limit.
        /// </summary>
        protected void IncreaseResourceDepth()
        {
            this.currentResourceDepth++;

            if (this.currentResourceDepth > this.inputContext.MessageReaderSettings.MessageQuotas.MaxNestingDepth)
            {
                throw new ODataException(Strings.ValidationUtils_MaxDepthOfNestedEntriesExceeded(this.inputContext.MessageReaderSettings.MessageQuotas.MaxNestingDepth));
            }
        }

        /// <summary>
        /// Decrements the nested entry count by one.
        /// </summary>
        protected void DecreaseResourceDepth()
        {
            Debug.Assert(this.currentResourceDepth > 0, "Resource depth should never become negative.");

            this.currentResourceDepth--;
        }

        private bool ReadAtNavigationLinkStartImplementationSynchronously()
        {
            if (this.jsonReader.NodeType == JsonNodeType.StartArray)
            {
                // '[' resource set
                ODataResourceSet fd = new ODataResourceSet();
                this.ApplyResourceSetStartingAnnotationsTo(fd);
                this.EnterScope(new Scope(ODataReaderState.ResourceSetStart, fd, this.CurrentNavigationSource, this.CurrentEntityType, new ODataUri()));
                return true;
            }
            else if (this.jsonReader.NodeType == JsonNodeType.StartObject)
            {
                // '{' resource
                this.EnterScope(new ResourceScope(this.CurrentNavigationSource, this.CurrentEntityType, new ODataUri()));
                return true;
            }

            throw new NotImplementedException();
        }

        private bool ReadAtNavigationLinkEndImplementationSynchronously()
        {
            this.PopScope(ODataReaderState.NestedResourceInfoEnd);
            bool result = true;
            if (this.jsonReader.NodeType == JsonNodeType.Property)
            {
                // next property
                this.ContiueReadingResource(false);
            }
            else if (this.jsonReader.NodeType == JsonNodeType.EndObject)
            {
                // '}'
                if (this.CurrentScope.State == ODataReaderState.ResourceStart)
                {
                    this.ContiueReadingResource(false);
                }
                else
                {
                    throw new InvalidOperationException();
                }
            }
            else
            {
                throw new InvalidOperationException();
            }

            return result;
        }

        private bool PreRootResourceSetStart()
        {
            bool result;
            Dictionary<string, ODataUntypedValue> annotations = new Dictionary<string, ODataUntypedValue>();
            this.jsonReader.ReadStartObject(); // first '{'
            ODataResourceSet fd = new ODataResourceSet();
            fd.SetAnnotation(annotations);
            while (this.jsonReader.NodeType == JsonNodeType.Property)
            {
                string name = (string)this.jsonReader.Value;
                if (string.Equals(name, JsonLightConstants.ODataValuePropertyName, StringComparison.OrdinalIgnoreCase))
                {
                    result = this.jsonReader.Read(); // at '[' - "value": [
                    Debug.Assert(this.jsonReader.NodeType == JsonNodeType.StartArray,
                        "this.jsonReader.NodeType == JsonNodeType.StartArray");
                    this.EnterScope(new Scope(ODataReaderState.ResourceSetStart, fd, null, this.CurrentEntityType, new ODataUri()));
                    break;
                }
                else
                {
                    result = this.jsonReader.Read(); // expecting annotation
                    ODataUntypedValue untypedVal = this.jsonReader.ReadAsUntypedOrNullValue();
                    int pos = name.IndexOf('@');
                    if (pos == 0)
                    {
                        ODataJsonLiteReaderUtils.ApplyFeedInstanceAnnotation(this.inputContext, ref this.contextUriParseResult, this.CurrentScope, fd, "", name, untypedVal);
                    }
                    else if (pos > 0)
                    {
                        annotations.Add(name, untypedVal);
                    }
                    else
                    {
                        throw new InvalidOperationException();
                    }
                }
            }

            result = true;
            return result;
        }

        private bool ReadAtResourceSetStartImplementationSynchronously()
        {
            this.jsonReader.ReadStartArray();
            if (this.jsonReader.NodeType == JsonNodeType.StartObject)
            {
                this.EnterScope(new ResourceScope(this.CurrentNavigationSource, this.CurrentEntityType, new ODataUri()));
            }
            else if (this.jsonReader.NodeType == JsonNodeType.EndArray)
            {
                EndCurrentResourceSet();
            }
            else
            {
                throw new InvalidOperationException();
            }

            return true;
        }

        private bool ReadAtResourceSetEndImplementationSynchronously()
        {
            this.PopScope(ODataReaderState.ResourceSetEnd);
            bool result = true;

            // (1) this resourceSet is the root, no parent NestedResourceInfo.
            // (2) or there is a parent NestedResourceInfo for this resourceSet.
            if (this.scopes.Count == 1)
            {
                this.jsonReader.ReadEndObject();
                this.ReplaceScope(ODataReaderState.Start, ODataReaderState.Completed);
                result = false;
            }
            else
            {
                this.ReplaceScope(ODataReaderState.NestedResourceInfoStart, ODataReaderState.NestedResourceInfoEnd);
            }

            return result;
        }

        private void EndCurrentResourceSet()
        {
            ODataResourceSet resourceSet = (ODataResourceSet)this.CurrentScope.Item;
            this.jsonReader.ReadEndArray();
            if (this.scopes.Count == 2)
            {
                // (1) this resourceSet is the root, no parent NestedResourceInfo.
                this.PostRootResourceSetEnd(resourceSet);
            }
            else
            {
                // (2) or there is a parent NestedResourceInfo for this resourceSet.
                Debug.Assert(this.scopes.Count > 2, "this.scopes.Count > 2");
                ODataNestedResourceInfo nestedInfo = (ODataNestedResourceInfo)(this.scopes.Skip(1).First().Item);
                ODataJsonLiteReaderUtils.ReadCurrentResourceSetEndingAnnotations(
                            (ODataJsonLightInputContext)this.inputContext, ref this.contextUriParseResult, this.CurrentScope, resourceSet, nestedInfo);
            }

            this.ReplaceScope(ODataReaderState.ResourceSetStart, ODataReaderState.ResourceSetEnd);
        }

        private void PostRootResourceSetEnd(ODataResourceSet resourceSet)
        {
            // read annotations
            while (this.jsonReader.NodeType == JsonNodeType.Property)
            {
                string name = (string)this.jsonReader.Value;
                this.jsonReader.Read();
                ODataUntypedValue untypedValue = this.jsonReader.ReadAsUntypedOrNullValue();
                if (name[0] == '@')
                {
                    Uri metadataDocumentUri = contextUriParseResult != null && contextUriParseResult.MetadataDocumentUri != null ? contextUriParseResult.MetadataDocumentUri : null;
                    ODataJsonLiteReaderUtils.ApplyFeedInstanceAnnotation(this.inputContext, ref this.contextUriParseResult, this.CurrentScope, resourceSet, "", name, untypedValue);
                }
                else
                {
                    throw new InvalidOperationException();
                }
            }

            Debug.Assert(this.jsonReader.NodeType == JsonNodeType.EndObject, "this.jsonReader.NodeType == JsonNodeType.EndObject");
        }

        private bool ContiueReadingResource(bool expectingOnStartObject)
        {
            ResourceScope resourceScope = (ResourceScope)this.scopes.Peek();
            ODataResource entry = (ODataResource)resourceScope.Item;
            List<ODataProperty> properties = entry.Properties as List<ODataProperty>;
            if (properties == null)
            {
                properties = new List<ODataProperty>();
                entry.Properties = properties;
            }

            Dictionary<string, ODataUntypedValue> annotations = entry.GetAnnotation<Dictionary<string, ODataUntypedValue>>();
            if (annotations == null)
            {
                annotations = new Dictionary<string, ODataUntypedValue>();
                entry.SetAnnotation(annotations);
            }

            if (expectingOnStartObject)
            {
                this.jsonReader.ReadStartObject();  // {
            }

            bool finishedReadingResource = true;
            string lastODataTypeValue = null;
            while (this.jsonReader.NodeType == JsonNodeType.Property)
            {
                string name = (string)this.jsonReader.Value;
                this.jsonReader.Read();
                int pos = name.IndexOf('@');
                if (pos >= 0)
                {
                    ODataUntypedValue valTmp = this.jsonReader.ReadAsUntypedOrNullValue();
                    annotations.Add(name, valTmp);
                    if (name.EndsWith("@odata.type", StringComparison.OrdinalIgnoreCase))
                    {
                        lastODataTypeValue = (string)(((ODataUntypedPrimitiveValue)valTmp).Value);
                    }

                    if (pos == 0)
                    {
                        ApplyEntryInstanceAnnotation(this.inputContext, resourceScope, entry, name, valTmp);
                    }
                }
                else
                {
                    IEdmProperty edmProperty = ODataJsonLiteReaderUtils.FindEntityProperty(resourceScope, name);
                    object valueTmp = null;
                    if (this.jsonReader.NodeType == JsonNodeType.StartObject)
                    {
                        // nested entry or geo primitive
                        if ((edmProperty == null))
                        {
                            valueTmp = this.ReadPrimitiveEnumOrCollectionValue(name, null, lastODataTypeValue);
                        }
                        else if (edmProperty.Type.IsPrimitive())
                        {
                            valueTmp = this.ReadPrimitiveEnumOrCollectionValue(name, edmProperty.Type, lastODataTypeValue);
                        }
                        else
                        {
                            Debug.Assert(!edmProperty.Type.IsEnum(), "!edmProperty.Type.IsEnum()");
                            finishedReadingResource = false;
                            ODataNestedResourceInfo nestedResourceInfo = new ODataNestedResourceInfo()
                            {
                                Name = name,
                                IsCollection = false,
                            };

                            IEdmStructuredType complexEntityType = (IEdmStructuredType)((IEdmStructuredTypeReference)edmProperty.Type).Definition;
                            this.EnterScope(new NestedResourceInfoScope(nestedResourceInfo, this.CurrentNavigationSource, complexEntityType, new ODataUri()));
                            lastODataTypeValue = null;
                            break;
                        }
                    }
                    else if (this.jsonReader.NodeType == JsonNodeType.StartArray)
                    {
                        // nested expanded feed or primitive collection
                        if (edmProperty == null)
                        {
                            valueTmp = this.ReadPrimitiveEnumOrCollectionValue(name, null, lastODataTypeValue);
                        }
                        else if (ODataJsonLiteReaderUtils.IsPrimitiveEnumOrCollection(edmProperty.Type))
                        {
                            // primive collection
                            valueTmp = this.ReadPrimitiveEnumOrCollectionValue(name, edmProperty.Type, lastODataTypeValue);
                        }
                        else
                        {
                            // nested expanded feed
                            Debug.Assert(edmProperty.Type.IsCollection(), "edmProperty.Type.IsCollection()");
                            Debug.Assert(this.jsonReader.NodeType == JsonNodeType.StartArray, "this.jsonReader.NodeType == JsonNodeType.StartArray");

                            finishedReadingResource = false;
                            ODataNestedResourceInfo nestedResourceInfo = new ODataNestedResourceInfo()
                            {
                                Name = name,
                                IsCollection = true,
                            };

                            IEdmStructuredType complexEntityType = (IEdmStructuredType)(((IEdmCollectionTypeReference)edmProperty.Type).ElementType().Definition);
                            this.EnterScope(new NestedResourceInfoScope(nestedResourceInfo, this.CurrentNavigationSource, complexEntityType, new ODataUri()));
                            lastODataTypeValue = null;
                            break;
                        }
                    }
                    else
                    {
                        Debug.Assert(this.jsonReader.NodeType == JsonNodeType.PrimitiveValue,
                            "this.jsonReader.NodeType == JsonNodeType.PrimitiveValue");
                        if (edmProperty == null)
                        {
                            valueTmp = this.ReadPrimitiveEnumOrCollectionValue(name, null, lastODataTypeValue);
                        }
                        else
                        {
                            valueTmp = this.ReadPrimitiveEnumOrCollectionValue(name, edmProperty.Type, lastODataTypeValue);
                        }
                    }

                    lastODataTypeValue = null;
                    properties.Add(new ODataProperty { Name = name, Value = valueTmp });
                }
            }

            if (finishedReadingResource)
            {
                this.jsonReader.ReadEndObject();
                this.EndCurrentResource();
            }

            return true;
        }

        private bool ReadAtResourceEndImplementationSynchronously()
        {
            bool result = true;
            this.PopScope(ODataReaderState.ResourceEnd);
            if (this.jsonReader.NodeType == JsonNodeType.StartObject)
            {
                // next sibling entry
                this.EnterScope(new ResourceScope(this.CurrentNavigationSource, this.CurrentEntityType, new ODataUri()));
            }
            else if (this.jsonReader.NodeType == JsonNodeType.EndArray)
            {
                // ']' resourceSet ends
                EndCurrentResourceSet();
            }
            else if (this.jsonReader.NodeType == JsonNodeType.EndOfInput)
            {
                this.ReplaceScope(ODataReaderState.Start, ODataReaderState.Completed);
                result = false;
            }
            else if (this.jsonReader.NodeType == JsonNodeType.Property)
            {
                // next property
                this.ReplaceScope(ODataReaderState.NestedResourceInfoStart, ODataReaderState.NestedResourceInfoEnd);
            }
            else if (this.jsonReader.NodeType == JsonNodeType.EndObject)
            {
                // '}'
                if (this.CurrentScope.State == ODataReaderState.NestedResourceInfoStart)
                {
                    this.ReplaceScope(ODataReaderState.NestedResourceInfoStart, ODataReaderState.NestedResourceInfoEnd);
                }
                else if (this.CurrentScope.State == ODataReaderState.Start)
                {
                    this.jsonReader.ReadEndObject();
                    this.ReplaceScope(ODataReaderState.Start, ODataReaderState.Completed);
                    result = false;
                }
                else
                {
                    throw new InvalidOperationException();
                }
            }
            else
            {
                throw new InvalidOperationException();
            }

            return result;
        }

        /// <summary>
        /// Catch any exception thrown by the action passed in; in the exception case move the reader into
        /// state ExceptionThrown and then rethrow the exception.
        /// </summary>
        /// <typeparam name="T">The type returned from the <paramref name="action"/> to execute.</typeparam>
        /// <param name="action">The action to execute.</param>
        /// <returns>The result of executing the <paramref name="action"/>.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("DataWeb.Usage", "AC0014", Justification = "Throws every time")]
        private T InterceptException<T>(Func<T> action)
        {
            try
            {
                return action();
            }
            catch (Exception e)
            {
                if (ExceptionUtils.IsCatchableExceptionType(e))
                {
                    this.EnterScope(new Scope(ODataReaderState.Exception, null, null, null, null));
                }

                throw;
            }
        }

        /// <summary>
        /// Verifies that calling Read is valid.
        /// </summary>
        /// <param name="synchronousCall">true if the call is to be synchronous; false otherwise.</param>
        private void VerifyCanRead(bool synchronousCall)
        {
            this.inputContext.VerifyNotDisposed();
            this.VerifyCallAllowed(synchronousCall);

            if (this.State == ODataReaderState.Exception || this.State == ODataReaderState.Completed)
            {
                throw new ODataException(Strings.ODataReaderCore_ReadOrReadAsyncCalledInInvalidState(this.State));
            }
        }

        /// <summary>
        /// Verifies that a call is allowed to the reader.
        /// </summary>
        /// <param name="synchronousCall">true if the call is to be synchronous; false otherwise.</param>
        private void VerifyCallAllowed(bool synchronousCall)
        {
            if (synchronousCall)
            {
                if (!this.inputContext.Synchronous)
                {
                    throw new ODataException(Strings.ODataReaderCore_SyncCallOnAsyncReader);
                }
            }
            else
            {
#if PORTABLELIB
                if (this.inputContext.Synchronous)
                {
                    throw new ODataException(Strings.ODataReaderCore_AsyncCallOnSyncReader);
                }
#else
                Debug.Assert(false, "Async calls are not allowed in this build.");
#endif
            }
        }

        /// <summary>
        /// Reads geo, enum and other primitive values
        /// </summary>
        /// <param name="propertyName">The property name.</param>
        /// <param name="expectedTypeReference">The expected type reference.</param>
        /// <param name="annotatedOdataType">The @odata.type if any.</param>
        /// <returns>ODataUntypedValue, ODataCollectionValue or a CLR primitive value.</returns>
        private object ReadPrimitiveEnumOrCollectionValue(string propertyName, IEdmTypeReference expectedTypeReference, string annotatedOdataType)
        {
            IEdmTypeReference targetTypeReference = expectedTypeReference;
            if (!string.IsNullOrEmpty(annotatedOdataType))
            {
                string payloadTypeName = ReaderUtils.AddEdmPrefixOfTypeName(ReaderUtils.RemovePrefixOfTypeName(annotatedOdataType));
                SerializationTypeNameAnnotation serializationTypeNameAnnotation;
                EdmTypeKind targetTypeKind;
                targetTypeReference = ReaderValidationUtils.ResolvePayloadTypeNameAndComputeTargetType(
                    EdmTypeKind.None,
                    /*defaultPrimitivePayloadType*/ null,
                    expectedTypeReference,
                    payloadTypeName,
                    this.inputContext.Model,
                    this.inputContext.MessageReaderSettings,
                    () =>
                    {
                        switch (this.jsonReader.NodeType)
                        {
                            case JsonNodeType.PrimitiveValue: return EdmTypeKind.Primitive;
                            case JsonNodeType.StartArray: return EdmTypeKind.Collection;
                            default: return EdmTypeKind.Complex;
                        }
                    },
                    out targetTypeKind,
                    out serializationTypeNameAnnotation);
            }

            switch (this.jsonReader.NodeType)
            {
                case JsonNodeType.StartObject:
                    {
                        if (targetTypeReference != null)
                        {
                            if (targetTypeReference.IsGeographyType() || targetTypeReference.IsGeometryType())
                            {
                                ISpatial result = ODataJsonReaderCoreUtils.ReadSpatialValue(
                                    this.jsonReader,
                                    false, // insideJsonObjectValue
                                    this.inputContext,
                                    (IEdmPrimitiveTypeReference)targetTypeReference,
                                    true,  // validateNullValue
                                    this.currentResourceDepth,
                                    propertyName);
                                return /*new ODataPrimitiveValue*/(result);
                            }
                            else
                            {
                                throw new InvalidOperationException();
                            }
                        }
                        else
                        {
                            this.ReadUndeclaredPropertyValue();
                            return new ODataNullValue();
                        }
                    }

                case JsonNodeType.StartArray:
                    {
                        this.jsonReader.ReadStartArray();
                        IEdmTypeReference itemTypeReference =
                            (targetTypeReference == null) ? null : targetTypeReference.GetCollectionItemType();
                        List<object> collectionItems = new List<object>();
                        while (this.jsonReader.NodeType != JsonNodeType.EndArray)
                        {
                            object valTmp = this.ReadPrimitiveEnumOrCollectionValue(propertyName, itemTypeReference, null);
                            collectionItems.Add(valTmp);
                        }

                        this.jsonReader.ReadEndArray();
                        ODataCollectionValue val = new ODataCollectionValue()
                        {
                            TypeName = (targetTypeReference == null) ? null : targetTypeReference.FullName(),
                            Items = collectionItems
                        };

                        return val;
                    }

                case JsonNodeType.PrimitiveValue:
                    {
                        object val = this.jsonReader.Value;
                        this.jsonReader.Read();
                        if (targetTypeReference == null)
                        {
                            return /*new ODataPrimitiveValue*/(val);
                        }
                        else if (val == null)
                        {
                            return /*new ODataNullValue()*/ null;
                        }
                        else if (targetTypeReference.IsEnum())
                        {
                            return new ODataEnumValue(val.ToString(), targetTypeReference.AsEnum().FullName());
                        }
                        else
                        {
                            object result = ODataJsonLightReaderUtils.ConvertValue(
                                val,
                                (IEdmPrimitiveTypeReference)targetTypeReference,
                                this.inputContext.MessageReaderSettings,
                                false, /*validateNullValue,*/
                                propertyName,
                                this.inputContext.PayloadValueConverter);
                            return /*new ODataPrimitiveValue*/(result);
                        }
                    }

                default:
                    throw new InvalidOperationException("Unexpected " + this.jsonReader.NodeType);
            }
        }

        private void ReadUndeclaredPropertyValue()
        {
            // TODO
            this.jsonReader.SkipValue();
        }

        /// <summary>
        /// A reader scope; keeping track of the current reader state and an item associated with this state.
        /// </summary>
        internal class Scope
        {
            /// <summary>The item attached to this scope.</summary>
            private readonly ODataItem item;

            /// <summary>The odataUri parsed based on the context uri attached to this scope.</summary>
            private readonly ODataUri odataUri;

            /// <summary>The reader state of this scope.</summary>
            private ODataReaderState state;

            /// <summary>
            /// Constructor creating a new reader scope.
            /// </summary>
            /// <param name="state">The reader state of this scope.</param>
            /// <param name="item">The item attached to this scope.</param>
            /// <param name="navigationSource">The navigation source we are going to read entities for.</param>
            /// <param name="expectedEntityType">The expected entity type for the scope.</param>
            /// <param name="odataUri">The odataUri parsed based on the context uri for current scope</param>
            [SuppressMessage("Microsoft.Performance", "CA1800:DoNotCastUnnecessarily", Justification = "Debug.Assert check only.")]
            internal Scope(ODataReaderState state, ODataItem item, IEdmNavigationSource navigationSource, IEdmStructuredType expectedEntityType, ODataUri odataUri)
            {
                Debug.Assert(
                    state == ODataReaderState.Exception && item == null ||
                    state == ODataReaderState.ResourceStart && (item == null || item is ODataResource) ||
                    state == ODataReaderState.ResourceEnd && (item == null || item is ODataResource) ||
                    state == ODataReaderState.ResourceSetStart && item is ODataResourceSet ||
                    state == ODataReaderState.ResourceSetEnd && item is ODataResourceSet ||
                    state == ODataReaderState.NestedResourceInfoStart && item is ODataNestedResourceInfo ||
                    state == ODataReaderState.NestedResourceInfoEnd && item is ODataNestedResourceInfo ||
                    state == ODataReaderState.EntityReferenceLink && item is ODataEntityReferenceLink ||
                    state == ODataReaderState.Start && item == null ||
                    state == ODataReaderState.Completed && item == null,
                    "Reader state and associated item do not match.");
                Debug.Assert(state == ODataReaderState.Completed
                    || expectedEntityType is IEdmEntityType || expectedEntityType is IEdmComplexType, "expectedEntityType must have value unless completed.");
                this.state = state;
                this.item = item;
                this.EntityType = expectedEntityType;
                this.NavigationSource = navigationSource;
                this.odataUri = odataUri;
            }

            /// <summary>
            /// The reader state of this scope.
            /// </summary>
            internal ODataReaderState State
            {
                get
                {
                    return this.state;
                }

                set
                {
                    this.state = value;
                }
            }

            /// <summary>
            /// The item attached to this scope.
            /// </summary>
            internal ODataItem Item
            {
                get
                {
                    return this.item;
                }
            }

            /// <summary>
            /// The odataUri parsed based on the context url to this scope.
            /// </summary>
            internal ODataUri ODataUri
            {
                get
                {
                    return this.odataUri;
                }
            }

            /// <summary>
            /// The navigation source we are reading entries from (possibly null).
            /// </summary>
            internal IEdmNavigationSource NavigationSource { get; set; }

            /// <summary>
            /// The entity type for this scope. Can be either the expected one if the real one
            /// was not found yet, or the one specified in the payload itself (the real one).
            /// </summary>
            internal IEdmStructuredType EntityType { get; set; }
        }


        /// <summary>
        /// ResourceScope scope
        /// </summary>
        internal sealed class ResourceScope : Scope
        {
            private Dictionary<string, ODataResource> navigationResourceProperties =
                new Dictionary<string, ODataResource>();

            public ResourceScope(IEdmNavigationSource navigationSource, IEdmStructuredType expectedEntityType, ODataUri odataUri)
                : base(ODataReaderState.ResourceStart, new ODataResource(), navigationSource, expectedEntityType, odataUri)
            {
                if (this.EntityType != null)
                {
                    ((ODataResource)this.Item).TypeName = this.EntityType.FullTypeName();
                }
            }

            public IEdmStructuredType AnnotatedComplexEntityType { get; set; }

            public Dictionary<string, ODataResource> NavigationResourceProperties
            {
                get
                {
                    return this.navigationResourceProperties;
                }
            }
        }

        /// <summary>
        /// NestedResourceInfo Scope
        /// </summary>
        internal sealed class NestedResourceInfoScope : Scope
        {
            public NestedResourceInfoScope(ODataNestedResourceInfo nestedResourceInfo, IEdmNavigationSource navigationSource, IEdmStructuredType expectedEntityType, ODataUri odataUri)
                : base(ODataReaderState.NestedResourceInfoStart, nestedResourceInfo, navigationSource, expectedEntityType, odataUri)
            {
            }
        }
    }
}