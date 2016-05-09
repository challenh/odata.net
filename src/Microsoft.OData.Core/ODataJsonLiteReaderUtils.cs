//---------------------------------------------------------------------
// <copyright file="ODataJsonLiteReaderUtils.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for deltaResourceSetlicense information.
// </copyright>
//---------------------------------------------------------------------
using System;
using System.Diagnostics;
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
        public static ODataReader CreateODataReader(
            ODataJsonLightInputContext jsonLightInputContext,
            IEdmNavigationSource navigationSource,
            IEdmEntityType expectedEntityType,
            bool readingResourceSet,
            bool readingParameter = false,
            bool readingDelta = false,
            IODataReaderWriterListener listener = null)
        {
            if (jsonLightInputContext.MessageReaderSettings.MetadataValidationLevel == MetadataValidationLevel.Full)
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
            IEdmProperty property = entityType.FindProperty(propertyName);
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
            Uri metadataDocumentUri = (contextUriParseResult == null) ? null : contextUriParseResult.MetadataDocumentUri;

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

        internal static void ApplyFeedInstanceAnnotation(ODataJsonLightInputContext inputContext, ref ODataJsonLightContextUriParseResult contextUriParseResult, ODataJsonLightReader.Scope currentScope, ODataResourceSet resourceSet, string NavLinkName, string annotationName, ODataUntypedValue annotationValue)
        {
            Uri metadataDocumentUri = contextUriParseResult == null ? null : contextUriParseResult.MetadataDocumentUri;
            ODataMessageReaderSettings readerSettings = inputContext.MessageReaderSettings;
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

                        long countValue = (long)(int)(((ODataUntypedPrimitiveValue)annotationValue).Value);
                        resourceSet.Count = countValue; // TODO IEEE756
                    }

                    break;
                case ODataAnnotationNames.ODataDeltaLink:
                    {
                        if (resourceSet.DeltaLink != null)
                        {
                            throw new ODataException(Strings.DuplicatePropertyNamesChecker_DuplicateAnnotationNotAllowed(annotationName));
                        }

                        stringValue = (string)(((ODataUntypedPrimitiveValue)annotationValue).Value);
                        resourceSet.DeltaLink = ODataJsonLiteReaderUtils.ProcessUriFromPayload(stringValue, metadataDocumentUri, inputContext);
                    }

                    break;
                case ODataAnnotationNames.ODataContext:   // 'odata.context'
                    {
                        SetContextUri(ref contextUriParseResult, true, (string)(((ODataUntypedPrimitiveValue)annotationValue).Value),
                            inputContext.Model, inputContext.MessageReaderSettings, currentScope);
                    }

                    break;
                case ODataAnnotationNames.ODataNextLink:
                    if (resourceSet.NextPageLink != null)
                    {
                        throw new ODataException(Strings.DuplicatePropertyNamesChecker_DuplicateAnnotationNotAllowed(annotationName));
                    }

                    // Read the property value.
                    // resourceSet.NextPageLink = this.ReadAndValidateAnnotationStringValueAsUri(ODataAnnotationNames.ODataNextLink);
                    stringValue = (string)(((ODataUntypedPrimitiveValue)annotationValue).Value);
                    if (stringValue == null)
                    {
                        throw new ODataException(ODataErrorStrings.ODataJsonLightReaderUtils_AnnotationWithNullValue(annotationName));
                    }

                    resourceSet.NextPageLink = ProcessUriFromPayload(stringValue, metadataDocumentUri, inputContext);
                    break;

                // case ODataAnnotationNames.ODataDeltaLink:   // Delta links are not supported on expanded resource sets.
                default:
                    break;
            }
        }

        internal static void SetContextUri(ref ODataJsonLightContextUriParseResult contextUriParseResult, bool readingResourceSet, string url, IEdmModel model, ODataMessageReaderSettings messageReaderSettings, ODataJsonLightReader.Scope currentScope)
        {
            ODataPayloadKind payloadKind = readingResourceSet ? ODataPayloadKind.ResourceSet : ODataPayloadKind.Resource;
            contextUriParseResult = ODataJsonLightContextUriParser.Parse(model, url, payloadKind, messageReaderSettings.ReaderBehavior, true);
            currentScope.NavigationSource = contextUriParseResult.NavigationSource;
            currentScope.EntityType = (IEdmStructuredType)contextUriParseResult.EdmType;
        }

        /// <summary>
        /// Given a URI from the payload, this method will try to make it absolute, or fail otherwise.
        /// </summary>
        /// <param name="uriFromPayload">The URI string from the payload to process.</param>
        /// <param name="metadataDocumentUri">The metadata document URI.</param>
        /// <param name="inputContext">The ODataJsonLightInputContext.</param>
        /// <returns>An absolute URI to report.</returns>
        internal static Uri ProcessUriFromPayload(string uriFromPayload, Uri metadataDocumentUri, ODataJsonLightInputContext inputContext)
        {
            Debug.Assert(uriFromPayload != null, "uriFromPayload != null");

            Uri uri = new Uri(uriFromPayload, UriKind.RelativeOrAbsolute);

            // Try to resolve the URI using a custom URL resolver first.
            Uri resolvedUri = inputContext.ResolveUri(metadataDocumentUri, uri);
            if (resolvedUri != null)
            {
                return resolvedUri;
            }

            if (!uri.IsAbsoluteUri)
            {
                if (metadataDocumentUri == null)
                {
                    throw new ODataException(Strings.ODataJsonLightDeserializer_RelativeUriUsedWithouODataMetadataAnnotation(uriFromPayload, ODataAnnotationNames.ODataContext));
                }

                uri = UriUtils.UriToAbsoluteUri(metadataDocumentUri, uri);
            }

            Debug.Assert(uri.IsAbsoluteUri, "By now we should have an absolute URI.");
            return uri;
        }
    }
}
