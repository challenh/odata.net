﻿//---------------------------------------------------------------------
// <copyright file="ODataJsonLightPropertySerializer.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------

namespace Microsoft.OData.JsonLight
{
    #region Namespaces
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Linq;
    using Microsoft.OData.Edm;
    using Microsoft.OData.Metadata;
    using ODataErrorStrings = Microsoft.OData.Strings;
    #endregion Namespaces

    /// <summary>
    /// OData JsonLight serializer for properties.
    /// </summary>
    internal class ODataJsonLightPropertySerializer : ODataJsonLightSerializer
    {
        /// <summary>
        /// Serializer to use to write property values.
        /// </summary>
        private readonly ODataJsonLightValueSerializer jsonLightValueSerializer;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="jsonLightOutputContext">The output context to write to.</param>
        /// <param name="initContextUriBuilder">Whether contextUriBuilder should be initialized.</param>
        internal ODataJsonLightPropertySerializer(ODataJsonLightOutputContext jsonLightOutputContext, bool initContextUriBuilder = false)
            : base(jsonLightOutputContext, initContextUriBuilder)
        {
            this.jsonLightValueSerializer = new ODataJsonLightValueSerializer(this, initContextUriBuilder);
        }

        /// <summary>
        /// Gets the json light value writer.
        /// </summary>
        internal ODataJsonLightValueSerializer JsonLightValueSerializer
        {
            get
            {
                return this.jsonLightValueSerializer;
            }
        }

        /// <summary>
        /// Write an <see cref="ODataProperty" /> to the given stream. This method creates an
        /// async buffered stream and writes the property to it.
        /// </summary>
        /// <param name="property">The property to write.</param>
        internal void WriteTopLevelProperty(ODataProperty property)
        {
            Debug.Assert(property != null, "property != null");
            Debug.Assert(!(property.Value is ODataStreamReferenceValue), "!(property.Value is ODataStreamReferenceValue)");

            this.WriteTopLevelPayload(
                () =>
                {
                    this.JsonWriter.StartObjectScope();
                    ODataPayloadKind kind = this.JsonLightOutputContext.MessageWriterSettings.IsIndividualProperty ? ODataPayloadKind.IndividualProperty : ODataPayloadKind.Property;

                    ODataContextUrlInfo contextInfo = ODataContextUrlInfo.Create(property.ODataValue, this.JsonLightOutputContext.MessageWriterSettings.ODataUri, this.Model);
                    this.WriteContextUriProperty(kind, () => contextInfo);

                    // Note we do not allow named stream properties to be written as top level property.
                    this.JsonLightValueSerializer.AssertRecursionDepthIsZero();
                    this.WriteProperty(
                        property,
                        null /*owningType*/,
                        true /* isTopLevel */,
                        false /* allowStreamProperty */,
                        this.CreateDuplicatePropertyNamesChecker(),
                        null /* projectedProperties */);
                    this.JsonLightValueSerializer.AssertRecursionDepthIsZero();

                    this.JsonWriter.EndObjectScope();
                });
        }

        /// <summary>
        /// Writes property names and value pairs.
        /// </summary>
        /// <param name="owningType">The <see cref="IEdmStructuredType"/> of the resource (or null if not metadata is available).</param>
        /// <param name="properties">The enumeration of properties to write out.</param>
        /// <param name="isComplexValue">
        /// Whether the properties are being written for complex value. Also used for detecting whether stream properties
        /// are allowed as named stream properties should only be defined on ODataResource instances
        /// </param>
        /// <param name="duplicatePropertyNamesChecker">The checker instance for duplicate property names.</param>
        /// <param name="projectedProperties">Set of projected properties, or null if all properties should be written.</param>
        internal void WriteProperties(
            IEdmStructuredType owningType,
            IEnumerable<ODataProperty> properties,
            bool isComplexValue,
            DuplicatePropertyNamesChecker duplicatePropertyNamesChecker,
            ProjectedPropertiesAnnotation projectedProperties)
        {
            if (properties == null)
            {
                return;
            }

            foreach (ODataProperty property in properties)
            {
                this.WriteProperty(
                    property,
                    owningType,
                    false /* isTopLevel */,
                    !isComplexValue,
                    duplicatePropertyNamesChecker,
                    projectedProperties);
            }
        }

        /// <summary>
        /// Test to see if <paramref name="property"/> is an open property or not.
        /// </summary>
        /// <param name="property">The property in question.</param>
        /// <param name="owningType">The owning type of the property.</param>
        /// <param name="edmProperty">The metadata of the property.</param>
        /// <returns>true if the property is an open property; false if it is not, or if openness cannot be determined</returns>
        private bool IsOpenProperty(ODataProperty property, IEdmStructuredType owningType, IEdmProperty edmProperty)
        {
            Debug.Assert(property != null, "property != null");

            bool isOpenProperty;

            if (property.SerializationInfo != null)
            {
                isOpenProperty = property.SerializationInfo.PropertyKind == ODataPropertyKind.Open;
            }
            else
            {
                isOpenProperty = (!this.WritingResponse && owningType == null) // Treat property as dynamic property when writing request and owning type is null
                || (owningType != null && owningType.IsOpen && edmProperty == null);
            }

            if (isOpenProperty)
            {
                this.WriterValidator.ValidateOpenPropertyValue(property.Name, property.ODataValue);
            }

            return isOpenProperty;
        }

        /// <summary>
        /// Should write property or not.
        /// </summary>
        /// <param name="owningType">The IEdmStructuredType</param>
        /// <param name="property">The ODataProperty to be written.</param>
        /// <param name="edmProperty">The found edm information in model.</param>
        /// <param name="shouldWriteRawAnnotations">Outputs if should write raw annotations.</param>
        /// <returns>True if should write property.</returns>
        private bool ShouldWriteProperty(IEdmStructuredType owningType, ODataProperty property, IEdmProperty edmProperty, out bool shouldWriteRawAnnotations)
        {
            shouldWriteRawAnnotations = false;
            if (owningType == null)
            {
                return true; // for top level property
            }

            if (edmProperty != null)
            {
                return true; // has declared property name
            }

            // for undeclared property name:
            string propertyName = property.Name;
            if (owningType.IsOpen)
            {
                // when value type is known, return true;
                ODataComplexValue complexVal = property.Value as ODataComplexValue;
                if (complexVal != null && !string.IsNullOrEmpty(complexVal.TypeName))
                {
                    return true;
                }

                ODataCollectionValue collectionVal = property.Value as ODataCollectionValue;
                if (collectionVal != null && !string.IsNullOrEmpty(collectionVal.TypeName))
                {
                    return true;
                }

                if (!(property.Value is ODataUntypedValue))
                {
                    return true;
                }
            }

            // for non-open owning type, or for open owning type where value type is unknown, like ODataUntypedValue.
            if (this.MessageWriterSettings.ShouldSupportUndeclaredProperty())
            {
                shouldWriteRawAnnotations = true;
                return true;
            }

            Debug.Assert(
                this.MessageWriterSettings.ShouldThrowOnUndeclaredProperty(),
                "this.MessageWriterSettings.ShouldThrowOnUndeclaredProperty()");
            throw new ODataException(ODataErrorStrings.ValidationUtils_PropertyDoesNotExistOnType(propertyName, owningType.FullTypeName()));
        }

        /// <summary>
        /// Writes a name/value pair for a property.
        /// </summary>
        /// <param name="property">The property to write out.</param>
        /// <param name="owningType">The owning type for the <paramref name="property"/> or null if no metadata is available.</param>
        /// <param name="isTopLevel">true when writing a top-level property; false for nested properties.</param>
        /// <param name="allowStreamProperty">Should pass in true if we are writing a property of an ODataResource instance, false otherwise.
        /// Named stream properties should only be defined on ODataResource instances.</param>
        /// <param name="duplicatePropertyNamesChecker">The checker instance for duplicate property names.</param>
        /// <param name="projectedProperties">Set of projected properties, or null if all properties should be written.</param>
        [SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling", Justification = "Splitting the code would make the logic harder to understand; class coupling is only slightly above threshold.")]
        private void WriteProperty(
            ODataProperty property,
            IEdmStructuredType owningType,
            bool isTopLevel,
            bool allowStreamProperty,
            DuplicatePropertyNamesChecker duplicatePropertyNamesChecker,
            ProjectedPropertiesAnnotation projectedProperties)
        {
            this.WriterValidator.ValidatePropertyNotNull(property);

            string propertyName = property.Name;
            if (projectedProperties.ShouldSkipProperty(propertyName))
            {
                return;
            }

            this.WriterValidator.ValidatePropertyName(propertyName);
            duplicatePropertyNamesChecker.CheckForDuplicatePropertyNames(property);

            WriteInstanceAnnotation(property, isTopLevel);
            IEdmProperty edmProperty = WriterValidationUtils.ValidatePropertyDefined(
                propertyName,
                owningType,
                this.JsonLightOutputContext.MessageWriterSettings);

            string wirePropertyName = isTopLevel ? JsonLightConstants.ODataValuePropertyName : propertyName;
            IEdmTypeReference propertyTypeReference = edmProperty == null ? null : edmProperty.Type;
            ODataValue value = property.ODataValue;
            bool shouldWriteRawAnnotations = false;
            if (!ShouldWriteProperty(owningType, property, edmProperty, out shouldWriteRawAnnotations))
            {
                return;
            }

            bool alreadyWroteODataType = false;
            if (shouldWriteRawAnnotations)
            {
                TryWriteRawAnnotations(property, out alreadyWroteODataType);
            }

            // handle ODataUntypedValue
            ODataUntypedValue untypedValue = property.Value as ODataUntypedValue;
            if (untypedValue != null)
            {
                if (this.MessageWriterSettings.ShouldSupportUndeclaredProperty())
                {
                    this.JsonWriter.WriteName(wirePropertyName);
                    this.jsonLightValueSerializer.WriteUntypedValue(untypedValue);
                }

                return;
            }

            ODataStreamReferenceValue streamReferenceValue = value as ODataStreamReferenceValue;
            if (streamReferenceValue != null)
            {
                if (!allowStreamProperty)
                {
                    throw new ODataException(ODataErrorStrings.ODataWriter_StreamPropertiesMustBePropertiesOfODataEntry(propertyName));
                }

                Debug.Assert(owningType == null || owningType.IsODataEntityTypeKind(), "The metadata should not allow named stream properties to be defined on a non-entity type.");
                Debug.Assert(!isTopLevel, "Stream properties are not allowed at the top level.");
                this.WriterValidator.ValidateStreamReferenceProperty(property, edmProperty, this.WritingResponse);
                this.WriteStreamReferenceProperty(propertyName, streamReferenceValue);
                return;
            }

            if (value is ODataNullValue || value == null)
            {
                this.WriteNullProperty(property, propertyTypeReference, isTopLevel);
                return;
            }

            bool isOpenPropertyType = this.IsOpenProperty(property, owningType, edmProperty);

            ODataPrimitiveValue primitiveValue = value as ODataPrimitiveValue;
            if (primitiveValue != null)
            {
                this.WritePrimitiveProperty(property, primitiveValue, propertyTypeReference, isTopLevel, isOpenPropertyType, alreadyWroteODataType);
                return;
            }

            ODataComplexValue complexValue = value as ODataComplexValue;
            if (complexValue != null)
            {
                this.WriteComplexProperty(property, complexValue, propertyTypeReference, isTopLevel, isOpenPropertyType);
                return;
            }

            ODataEnumValue enumValue = value as ODataEnumValue;
            if (enumValue != null)
            {
                this.WriteEnumProperty(property, enumValue, propertyTypeReference, isTopLevel, isOpenPropertyType, alreadyWroteODataType);
                return;
            }

            ODataCollectionValue collectionValue = value as ODataCollectionValue;
            if (collectionValue != null)
            {
                this.WriteCollectionProperty(property, collectionValue, propertyTypeReference, isTopLevel, isOpenPropertyType, alreadyWroteODataType);
                return;
            }
        }

        /// <summary>
        /// Writes instance annotation for property
        /// </summary>
        /// <param name="property">The property to handle.</param>
        /// <param name="isTopLevel">If writing top level property.</param>
        private void WriteInstanceAnnotation(ODataProperty property, bool isTopLevel)
        {
            if (property.InstanceAnnotations.Any())
            {
                if (isTopLevel)
                {
                    this.InstanceAnnotationWriter.WriteInstanceAnnotations(property.InstanceAnnotations);
                }
                else
                {
                    this.InstanceAnnotationWriter.WriteInstanceAnnotations(property.InstanceAnnotations, property.Name);
                }
            }
        }

        /// <summary>
        /// Write raw annotatoins if hte property value has any.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <param name="isODataTypeWritten">Outputs if odata.type annotation has been written to the wire.</param>
        /// <returns>True if raw annotations have been written.</returns>
        private bool TryWriteRawAnnotations(ODataProperty property, out bool isODataTypeWritten)
        {
            ODataUntypedValue untypedValueTmp = property.Value as ODataUntypedValue;
            ODataAnnotatable annotatableValue = (ODataAnnotatable)untypedValueTmp ?? (ODataAnnotatable)property.ODataValue;
            isODataTypeWritten = false;
            if (annotatableValue != null)
            {
                ODataValueRawAnnotations tmpSet = annotatableValue.GetAnnotation<ODataValueRawAnnotations>();
                if (tmpSet != null && tmpSet.Annotations != null)
                {
                    foreach (KeyValuePair<string, ODataUntypedValue> kvp in tmpSet.Annotations)
                    {
                        bool isODataType =
                            string.Equals(kvp.Key, ODataAnnotationNames.ODataType, StringComparison.OrdinalIgnoreCase);
                        if (isODataType && (annotatableValue is ODataComplexValue))
                        {
                            continue; // skip odata.type for complex value
                        }

                        this.JsonWriter.WriteName(string.Format(CultureInfo.InvariantCulture, "{0}@{1}", property.Name, kvp.Key));
                        this.JsonWriter.WriteRawValue(kvp.Value.RawValue);
                        if (isODataType)
                        {
                            isODataTypeWritten = true;
                        }
                    }

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Writes a stream property.
        /// </summary>
        /// <param name="propertyName">The name of the property to write.</param>
        /// <param name="streamReferenceValue">The stream reference value to be written</param>
        private void WriteStreamReferenceProperty(string propertyName, ODataStreamReferenceValue streamReferenceValue)
        {
            Debug.Assert(!string.IsNullOrEmpty(propertyName), "!string.IsNullOrEmpty(propertyName)");
            Debug.Assert(streamReferenceValue != null, "streamReferenceValue != null");

            Uri mediaEditLink = streamReferenceValue.EditLink;
            if (mediaEditLink != null)
            {
                this.ODataAnnotationWriter.WritePropertyAnnotationName(propertyName, ODataAnnotationNames.ODataMediaEditLink);
                this.JsonWriter.WriteValue(this.UriToString(mediaEditLink));
            }

            Uri mediaReadLink = streamReferenceValue.ReadLink;
            if (mediaReadLink != null)
            {
                this.ODataAnnotationWriter.WritePropertyAnnotationName(propertyName, ODataAnnotationNames.ODataMediaReadLink);
                this.JsonWriter.WriteValue(this.UriToString(mediaReadLink));
            }

            string mediaContentType = streamReferenceValue.ContentType;
            if (mediaContentType != null)
            {
                this.ODataAnnotationWriter.WritePropertyAnnotationName(propertyName, ODataAnnotationNames.ODataMediaContentType);
                this.JsonWriter.WriteValue(mediaContentType);
            }

            string mediaETag = streamReferenceValue.ETag;
            if (mediaETag != null)
            {
                this.ODataAnnotationWriter.WritePropertyAnnotationName(propertyName, ODataAnnotationNames.ODataMediaETag);
                this.JsonWriter.WriteValue(mediaETag);
            }
        }

        /// <summary>
        /// Writes a Null property.
        /// </summary>
        /// <param name="property">The property to write out.</param>
        /// <param name="propertyTypeReference">The metadata type reference of the property.</param>
        /// <param name="isTopLevel">True when writing a top-level property; false for nested properties.</param>
        private void WriteNullProperty(
            ODataProperty property,
            IEdmTypeReference propertyTypeReference,
            bool isTopLevel)
        {
            this.WriterValidator.ValidateNullPropertyValue(propertyTypeReference, property.Name, this.MessageWriterSettings.WriterBehavior, this.Model);

            if (isTopLevel)
            {
                // Write the special null marker for top-level null properties.
                this.ODataAnnotationWriter.WriteInstanceAnnotationName(ODataAnnotationNames.ODataNull);
                this.JsonWriter.WriteValue(true);
            }
            else
            {
                this.JsonWriter.WriteName(property.Name);
                this.JsonLightValueSerializer.WriteNullValue();
            }
        }

        /// <summary>
        /// Writes a complex property.
        /// </summary>
        /// <param name="property">The property to write out.</param>
        /// <param name="complexValue">The complex value to be written</param>
        /// <param name="propertyTypeReference">The metadata type reference of the property.</param>
        /// <param name="isTopLevel">True when writing a top-level property; false for nested properties.</param>
        /// <param name="isOpenPropertyType">If the property is open.</param>
        private void WriteComplexProperty(
            ODataProperty property,
            ODataComplexValue complexValue,
            IEdmTypeReference propertyTypeReference,
            bool isTopLevel,
            bool isOpenPropertyType)
        {
            if (!isTopLevel)
            {
                this.JsonWriter.WriteName(property.Name);
            }

            this.JsonLightValueSerializer.WriteComplexValue(complexValue, propertyTypeReference, isTopLevel, isOpenPropertyType, this.CreateDuplicatePropertyNamesChecker());
        }

        /// <summary>
        /// Writes a enum property.
        /// </summary>
        /// <param name="property">The property to write out.</param>
        /// <param name="enumValue">The enum value to be written.</param>
        /// <param name="propertyTypeReference">The metadata type reference of the property.</param>
        /// <param name="isTopLevel">true when writing a top-level property; false for nested properties.</param>
        /// <param name="isOpenPropertyType">If the property is open.</param>
        /// <param name="alreadyWroteODataType">If the property's @odata.type is already writtern.</param>
        private void WriteEnumProperty(
            ODataProperty property,
            ODataEnumValue enumValue,
            IEdmTypeReference propertyTypeReference,
            bool isTopLevel,
            bool isOpenPropertyType,
            bool alreadyWroteODataType)
        {
            string wirePropertyName = GetWirePropertyName(isTopLevel, property.Name);
            if (!alreadyWroteODataType)
            {
                IEdmTypeReference typeFromValue = TypeNameOracle.ResolveAndValidateTypeForEnumValue(this.Model, enumValue, isOpenPropertyType);

                // This is a work around, needTypeOnWire always = true for client side: 
                // ClientEdmModel's reflection can't know a property is open type even if it is, so here 
                // make client side always write 'odata.type' for enum.
                bool needTypeOnWire = string.Equals(this.JsonLightOutputContext.Model.GetType().Name, "ClientEdmModel", StringComparison.OrdinalIgnoreCase);
                string typeNameToWrite = this.JsonLightOutputContext.TypeNameOracle.GetValueTypeNameForWriting(
                    enumValue, propertyTypeReference, typeFromValue, needTypeOnWire || isOpenPropertyType);

                this.WritePropertyTypeName(wirePropertyName, typeNameToWrite, isTopLevel);
            }

            this.JsonWriter.WriteName(wirePropertyName);
            this.JsonLightValueSerializer.WriteEnumValue(enumValue, propertyTypeReference);
        }

        /// <summary>
        /// Writes a collection property.
        /// </summary>
        /// <param name="property">The property to write out.</param>
        /// <param name="collectionValue">The collection value to be written</param>
        /// <param name="propertyTypeReference">The metadata type reference of the property.</param>
        /// <param name="isTopLevel">true when writing a top-level property; false for nested properties.</param>
        /// <param name="isOpenPropertyType">If the property is open.</param>
        /// <param name="alreadyWroteODataType">If the property's @odata.type is already writtern.</param>
        private void WriteCollectionProperty(
            ODataProperty property,
            ODataCollectionValue collectionValue,
            IEdmTypeReference propertyTypeReference,
            bool isTopLevel,
            bool isOpenPropertyType,
            bool alreadyWroteODataType)
        {
            string wirePropertyName = GetWirePropertyName(isTopLevel, property.Name);
            IEdmTypeReference typeFromValue = TypeNameOracle.ResolveAndValidateTypeForCollectionValue(this.Model, propertyTypeReference, collectionValue, isOpenPropertyType, this.WriterValidator);
            if (!alreadyWroteODataType)
            {
                string typeNameToWrite = this.JsonLightOutputContext.TypeNameOracle.GetValueTypeNameForWriting(collectionValue, propertyTypeReference, typeFromValue, isOpenPropertyType);
                this.WritePropertyTypeName(wirePropertyName, typeNameToWrite, isTopLevel);
            }

            this.JsonWriter.WriteName(wirePropertyName);

            // passing false for 'isTopLevel' because the outer wrapping object has already been written.
            this.JsonLightValueSerializer.WriteCollectionValue(collectionValue, propertyTypeReference, typeFromValue, isTopLevel, false /*isInUri*/, isOpenPropertyType);
            return;
        }

        /// <summary>
        /// Writes a primitive property.
        /// </summary>
        /// <param name="property">The property to write out.</param>
        /// <param name="primitiveValue">The primitive value to be written</param>
        /// <param name="propertyTypeReference">The metadata type reference of the property.</param>
        /// <param name="isTopLevel">true when writing a top-level property; false for nested properties.</param>
        /// <param name="isOpenPropertyType">If the property is open.</param>
        /// <param name="alreadyWroteODataType">If the property's @odata.type is already writtern.</param>
        private void WritePrimitiveProperty(
            ODataProperty property,
            ODataPrimitiveValue primitiveValue,
            IEdmTypeReference propertyTypeReference,
            bool isTopLevel,
            bool isOpenPropertyType,
            bool alreadyWroteODataType)
        {
            string wirePropertyName = GetWirePropertyName(isTopLevel, property.Name);
            if (!alreadyWroteODataType)
            {
                IEdmTypeReference typeFromValue = TypeNameOracle.ResolveAndValidateTypeForPrimitiveValue(primitiveValue);
                string typeNameToWrite = this.JsonLightOutputContext.TypeNameOracle.GetValueTypeNameForWriting(primitiveValue, propertyTypeReference, typeFromValue, isOpenPropertyType);
                this.WritePropertyTypeName(wirePropertyName, typeNameToWrite, isTopLevel);
            }

            this.JsonWriter.WriteName(wirePropertyName);
            this.JsonLightValueSerializer.WritePrimitiveValue(primitiveValue.Value, propertyTypeReference);
        }

        /// <summary>
        /// Writes the type name on the wire.
        /// </summary>
        /// <param name="propertyName">Name of the property.</param>
        /// <param name="typeNameToWrite">Type name of the property.</param>
        /// <param name="isTopLevel">true when writing a top-level property; false for nested properties.</param>
        private void WritePropertyTypeName(string propertyName, string typeNameToWrite, bool isTopLevel)
        {
            if (typeNameToWrite != null)
            {
                // We write the type name as an instance annotation (named "odata.type") for top-level properties, but as a property annotation (e.g., "...@odata.type") if not top level.
                if (isTopLevel)
                {
                    this.ODataAnnotationWriter.WriteODataTypeInstanceAnnotation(typeNameToWrite);
                }
                else
                {
                    this.ODataAnnotationWriter.WriteODataTypePropertyAnnotation(propertyName, typeNameToWrite);
                }
            }
        }

        /// <summary>
        /// Determines the property name in wire
        /// </summary>
        /// <param name="isTopLevel">If the property is top level.</param>
        /// <param name="propertyName">The property name.</param>
        /// <returns>The property name will be written in wire</returns>
        private static string GetWirePropertyName(bool isTopLevel, string propertyName)
        {
            return isTopLevel ? JsonLightConstants.ODataValuePropertyName : propertyName;
        }
    }
}
