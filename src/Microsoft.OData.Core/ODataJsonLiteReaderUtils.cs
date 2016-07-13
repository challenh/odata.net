//---------------------------------------------------------------------
// <copyright file="ODataJsonLiteReaderUtils.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for deltaResourceSetlicense information.
// </copyright>
//---------------------------------------------------------------------
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.OData.Edm;
using Microsoft.OData.Json;
using Microsoft.OData.JsonLight;
using ODataErrorStrings = Microsoft.OData.Strings;

namespace Microsoft.OData
{
    /// <summary>
    /// Base class for Resource Set and Resource.
    /// </summary>
    internal static class ODataJsonLiteReaderUtils
    {
        public static object ParseJsonToPrimitiveValue(string rawValue)
        {
            Debug.Assert(rawValue != null && rawValue.Length > 0 && rawValue.IndexOf('{') != 0 && rawValue.IndexOf('[') != 0,
                  "rawValue != null && rawValue.Length > 0 && rawValue.IndexOf('{') != 0 && rawValue.IndexOf('[') != 0");
            ODataCollectionValue collectionValue = (ODataCollectionValue)
                Microsoft.OData.ODataUriUtils.ConvertFromUriLiteral(string.Format(CultureInfo.InvariantCulture, "[{0}]", rawValue), ODataVersion.V4);
            foreach (object item in collectionValue.Items)
            {
                return item;
            }

            return null;
        }

        public static ODataReader CreateODataReader(
            ODataJsonLightInputContext jsonLightInputContext,
            IEdmNavigationSource navigationSource,
            IEdmStructuredType expectedEntityType,
            bool readingResourceSet,
            bool readingParameter = false,
            bool readingDelta = false,
            IODataReaderWriterListener listener = null)
        {
            if (jsonLightInputContext.MessageReaderSettings.MetadataEnablingLevel == MetadataEnablingLevel.Full
                || readingParameter || readingDelta)
            {
                return new ODataJsonLightFullReader(
                    jsonLightInputContext,
                    navigationSource,
                    expectedEntityType,
                    readingResourceSet,
                    readingParameter,
                    readingDelta,
                    listener);
            }
            else
            {
                return new ODataJsonLightReader(
                    jsonLightInputContext,
                    navigationSource,
                    expectedEntityType,
                    readingResourceSet,
                    readingParameter,
                    readingDelta,
                    listener);
            }
        }

        public static bool IsPrimitiveEnumOrCollection(IEdmTypeReference typeReference)
        {
            if (typeReference.IsPrimitive() || typeReference.IsEnum())
            {
                return true;
            }

            IEdmCollectionTypeReference collectionTypeReference = typeReference as IEdmCollectionTypeReference;
            if (collectionTypeReference != null)
            {
                IEdmTypeReference elementTypeReference = collectionTypeReference.ElementType();
                return (elementTypeReference.IsPrimitive() || elementTypeReference.IsEnum());
            }

            return false;
        }

        public static IEdmProperty FindEntityProperty(ODataJsonLightReader.ResourceScope resourceScope, string propertyName)
        {
            IEdmStructuredType entityType = resourceScope.AnnotatedComplexEntityType ?? resourceScope.EntityType;
            IEdmProperty property = (entityType == null) ? null : entityType.FindProperty(propertyName);
            return property;
        }

        /// <summary>
        /// Checks if there are following annotation immediately after an expanded resource set, and reads and stores them.
        /// We fail here if we encounter any other property annotation for the expanded navigation (since these should come before the property itself).
        /// </summary>
        /// <param name="inputContext">The ODataJsonLightInputContext</param>
        /// <param name="contextUriParseResult">the ref ODataJsonLightContextUriParseResult</param>
        /// <param name="currentScope">The currentScope</param>
        /// <param name="resourceSet">The resourceSet</param>
        /// <param name="nestedResourceInfo">The ODataNestedResourceInfo</param>
        public static void ReadCurrentResourceSetEndingAnnotations(
            ODataJsonLightInputContext inputContext,
            ref ODataJsonLightContextUriParseResult contextUriParseResult,
            ODataJsonLightReader.Scope currentScope,
            ODataResourceSetBase resourceSet,
            ODataNestedResourceInfo nestedResourceInfo)
        {
            Debug.Assert(nestedResourceInfo.IsCollection == true, "Only collection navigation properties can have resourceSet content.");

            // Uri metadataDocumentUri = (contextUriParseResult == null) ? null : contextUriParseResult.MetadataDocumentUri;
            // Look at the next property in the owning resource, if it's a property annotation for the expanded nested resource info info property, read it.
            string propertyName, annotationName;
            while (inputContext.JsonReader.NodeType == JsonNodeType.Property &&
                   ODataJsonLightDeserializer.TryParsePropertyAnnotation(inputContext.JsonReader.GetPropertyName(), out propertyName, out annotationName) &&
                   string.CompareOrdinal(propertyName, nestedResourceInfo.Name) == 0)
            {
                if (!inputContext.ReadingResponse)
                {
                    throw new ODataException(ODataErrorStrings.ODataJsonLightPropertyAndValueDeserializer_UnexpectedPropertyAnnotation(propertyName, annotationName));
                }

                // Read over the property name.
                inputContext.JsonReader.Read();
                ODataUntypedValue untypedVal = inputContext.JsonReader.ReadAsUntypedOrNullValue();
                ApplyFeedInstanceAnnotation(inputContext, ref contextUriParseResult, currentScope, (ODataResourceSet)resourceSet, propertyName, annotationName, untypedVal);
            }
        }

        /// <summary>
        /// Applies Resource instance annotation.
        /// </summary>
        /// <param name="inputContext">The ODataInputContext.</param>
        /// <param name="contextUriParseResult">The ODataJsonLightContextUriParseResult.</param>
        /// <param name="resourceScope">The ResourceScope.</param>
        /// <param name="resource">The ODataResource.</param>
        /// <param name="annotationName">The name.</param>
        /// <param name="annotationValue">The value.</param>
        [SuppressMessage("Microsoft.Performance", "CA1800:DoNotCastUnnecessarily", Justification = "The casts aren't actually being done multiple times, since they occur in different cases of the switch statement.")]
        public static void ApplyEntryInstanceAnnotation(
            ODataJsonLightInputContext inputContext,
            ref ODataJsonLightContextUriParseResult contextUriParseResult,
            ODataJsonLightReader.ResourceScope resourceScope,
            ODataResource resource,
            string annotationName,
            ODataUntypedValue annotationValue)
        {
            Debug.Assert(resource != null, "resource != null");
            Debug.Assert(!string.IsNullOrEmpty(annotationName), "!string.IsNullOrEmpty(annotationName)");

            ODataStreamReferenceValue mediaResource = resource.MediaResource;
            if (annotationName[0] == '@')
            {
                annotationName = annotationName.Substring(1);
            }

            switch (ODataJsonLightDeserializer.CompleteSimplifiedODataAnnotation(inputContext, annotationName))
            {
                case ODataAnnotationNames.ODataType:   // 'odata.type'
                    {
                        string tmpValue = (string)ODataJsonLiteReaderUtils.ParseJsonToPrimitiveValue(((ODataUntypedValue)annotationValue).RawValue);
                        resource.TypeName = ReaderUtils.AddEdmPrefixOfTypeName(ReaderUtils.RemovePrefixOfTypeName(tmpValue));
                        resourceScope.AnnotatedComplexEntityType = (IEdmStructuredType)inputContext.Model.FindType(resource.TypeName);
                    }

                    break;
                case ODataAnnotationNames.ODataId:   // 'odata.id'
                    {
                        string tmpValue = (string)ODataJsonLiteReaderUtils.ParseJsonToPrimitiveValue(((ODataUntypedValue)annotationValue).RawValue);
                        if (tmpValue == null)
                        {
                            resource.IsTransient = true;
                        }
                        else
                        {
                            resource.Id = ConvertToUri(inputContext, contextUriParseResult, tmpValue);
                        }
                    }

                    break;
                case ODataAnnotationNames.ODataETag:   // 'odata.etag'
                    {
                        string tmpValue = (string)ODataJsonLiteReaderUtils.ParseJsonToPrimitiveValue(((ODataUntypedValue)annotationValue).RawValue);
                        resource.ETag = tmpValue;
                    }

                    break;
                case ODataAnnotationNames.ODataEditLink:    // 'odata.editLink'
                    {
                        string tmpValue = (string)ODataJsonLiteReaderUtils.ParseJsonToPrimitiveValue(((ODataUntypedValue)annotationValue).RawValue);
                        resource.EditLink = ConvertToUri(inputContext, contextUriParseResult, tmpValue);
                    }

                    break;
                case ODataAnnotationNames.ODataReadLink:    // 'odata.readLink'
                    {
                        string tmpValue = (string)ODataJsonLiteReaderUtils.ParseJsonToPrimitiveValue(((ODataUntypedValue)annotationValue).RawValue);
                        resource.ReadLink = ConvertToUri(inputContext, contextUriParseResult, tmpValue);
                    }

                    break;
                case ODataAnnotationNames.ODataMediaEditLink:   // 'odata.mediaEditLink'
                    {
                        string tmpValue = (string)ODataJsonLiteReaderUtils.ParseJsonToPrimitiveValue(((ODataUntypedValue)annotationValue).RawValue);
                        ODataJsonLightReaderUtils.EnsureInstance(ref mediaResource);
                        mediaResource.EditLink = ConvertToUri(inputContext, contextUriParseResult, tmpValue);
                    }

                    break;
                case ODataAnnotationNames.ODataMediaReadLink:   // 'odata.mediaReadLink'
                    {
                        string tmpValue = (string)ODataJsonLiteReaderUtils.ParseJsonToPrimitiveValue(((ODataUntypedValue)annotationValue).RawValue);
                        ODataJsonLightReaderUtils.EnsureInstance(ref mediaResource);
                        mediaResource.ReadLink = ConvertToUri(inputContext, contextUriParseResult, tmpValue);
                    }

                    break;
                case ODataAnnotationNames.ODataMediaContentType:  // 'odata.mediaContentType'
                    {
                        string tmpValue = (string)ODataJsonLiteReaderUtils.ParseJsonToPrimitiveValue(((ODataUntypedValue)annotationValue).RawValue);
                        ODataJsonLightReaderUtils.EnsureInstance(ref mediaResource);
                        mediaResource.ContentType = tmpValue;
                    }

                    break;
                case ODataAnnotationNames.ODataMediaETag:  // 'odata.mediaEtag'
                    {
                        string tmpValue = (string)ODataJsonLiteReaderUtils.ParseJsonToPrimitiveValue(((ODataUntypedValue)annotationValue).RawValue);
                        ODataJsonLightReaderUtils.EnsureInstance(ref mediaResource);
                        mediaResource.ETag = tmpValue;
                    }

                    break;
                case ODataAnnotationNames.ODataContext:  // 'odata.context'
                    {
                        string tmpValue = (string)ODataJsonLiteReaderUtils.ParseJsonToPrimitiveValue(((ODataUntypedValue)annotationValue).RawValue);
                        ODataJsonLiteReaderUtils.SetContextUri(ref contextUriParseResult, false, tmpValue,
                            inputContext.Model, inputContext.MessageReaderSettings, resourceScope);
                    }

                    break;
                default:
                    // TODO challenh: 

                    break;
            }

            if (mediaResource != null && resource.MediaResource == null)
            {
                // this.SetEntryMediaResource(resourceState, mediaResource);
            }
        }

        /// <summary>
        /// Gets a Uri from uriFromPayload. same as ReadAnnotationStringValueAsUri() method.
        /// </summary>
        /// <param name="inputContext">The ODataJsonLightInputContext.</param>
        /// <param name="contextUriParseResult">The ODataJsonLightContextUriParseResult or null.</param>
        /// <param name="uriFromPayload">The Uri string.</param>
        /// <returns>The Uri.</returns>
        internal static Uri ConvertToUri(
            ODataJsonLightInputContext inputContext,
            ODataJsonLightContextUriParseResult contextUriParseResult,
            string uriFromPayload)
        {
            if (inputContext.ReadingResponse)
            {
                Uri metadataDocumentUri = contextUriParseResult != null && contextUriParseResult.MetadataDocumentUri != null ? contextUriParseResult.MetadataDocumentUri : null;
                return ODataJsonLightDeserializer.ProcessUriFromPayload(inputContext, metadataDocumentUri, uriFromPayload);
            }
            else
            {
                return new Uri(uriFromPayload, UriKind.RelativeOrAbsolute);
            }
        }

        internal static void ApplyFeedInstanceAnnotation(ODataJsonLightInputContext inputContext, ref ODataJsonLightContextUriParseResult contextUriParseResult, ODataJsonLightReader.Scope currentScope, ODataResourceSet resourceSet, string NavLinkName, string annotationName, ODataUntypedValue annotationValue)
        {
            Uri metadataDocumentUri = contextUriParseResult == null ? null : contextUriParseResult.MetadataDocumentUri;
            Debug.Assert(resourceSet != null, "resourceSet != null");
            Debug.Assert(!string.IsNullOrEmpty(annotationName), "!string.IsNullOrEmpty(annotationName)");

            if (annotationName[0] == '@')
            {
                annotationName = annotationName.Substring(1);
            }

            string stringValue;
            switch (ODataJsonLightDeserializer.CompleteSimplifiedODataAnnotation(inputContext, annotationName))
            {
                case ODataAnnotationNames.ODataCount:   // 'odata.count'
                    {
                        if (resourceSet.Count != null)
                        {
                            throw new ODataException(
                                ODataErrorStrings.ODataJsonLightEntryAndFeedDeserializer_DuplicateExpandedFeedAnnotation(
                                    ODataAnnotationNames.ODataCount, NavLinkName));
                        }

                        long countValue = (long)(int)(ParseJsonToPrimitiveValue(annotationValue.RawValue));
                        resourceSet.Count = countValue; // TODO IEEE756
                    }

                    break;
                case ODataAnnotationNames.ODataDeltaLink:
                    {
                        if (resourceSet.DeltaLink != null)
                        {
                            throw new ODataException(Strings.DuplicateAnnotationNotAllowed(annotationName));
                        }

                        stringValue = (string)ParseJsonToPrimitiveValue(annotationValue.RawValue);
                        resourceSet.DeltaLink = ODataJsonLightDeserializer.ProcessUriFromPayload(inputContext, metadataDocumentUri, stringValue);
                    }

                    break;
                case ODataAnnotationNames.ODataContext:   // 'odata.context'
                    {
                        SetContextUri(ref contextUriParseResult, true, (string)ParseJsonToPrimitiveValue(annotationValue.RawValue),
                            inputContext.Model, inputContext.MessageReaderSettings, currentScope);
                    }

                    break;
                case ODataAnnotationNames.ODataNextLink:
                    if (resourceSet.NextPageLink != null)
                    {
                        throw new ODataException(Strings.DuplicateAnnotationNotAllowed(annotationName));
                    }

                    // Read the property value.
                    // resourceSet.NextPageLink = this.ReadAndValidateAnnotationStringValueAsUri(ODataAnnotationNames.ODataNextLink);
                    stringValue = (string)ParseJsonToPrimitiveValue(annotationValue.RawValue);
                    if (stringValue == null)
                    {
                        throw new ODataException(ODataErrorStrings.ODataJsonLightReaderUtils_AnnotationWithNullValue(annotationName));
                    }

                    resourceSet.NextPageLink = ODataJsonLightDeserializer.ProcessUriFromPayload(inputContext, metadataDocumentUri, stringValue);
                    break;

                // case ODataAnnotationNames.ODataDeltaLink:   // Delta links are not supported on expanded resource sets.
                default:
                    break;
            }
        }

        internal static void SetContextUri(ref ODataJsonLightContextUriParseResult contextUriParseResult, bool readingResourceSet, string url, IEdmModel model, ODataMessageReaderSettings messageReaderSettings, ODataJsonLightReader.Scope currentScope)
        {
            ODataPayloadKind payloadKind = readingResourceSet ? ODataPayloadKind.ResourceSet : ODataPayloadKind.Resource;
            contextUriParseResult = ODataJsonLightContextUriParser.Parse(model, url, payloadKind, messageReaderSettings.ClientCustomTypeResolver, true);
            currentScope.NavigationSource = contextUriParseResult.NavigationSource;
            currentScope.EntityType = (IEdmStructuredType)contextUriParseResult.EdmType;
        }
    }
}
