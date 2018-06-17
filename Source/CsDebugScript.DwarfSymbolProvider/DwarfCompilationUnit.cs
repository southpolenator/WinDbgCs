﻿using System;
using System.Collections.Generic;

namespace CsDebugScript.DwarfSymbolProvider
{
    /// <summary>
    /// DWARF compilation unit instance.
    /// </summary>
    internal class DwarfCompilationUnit
    {
        /// <summary>
        /// The dictionary of symbols located by offset in the debug data stream.
        /// </summary>
        private Dictionary<int, DwarfSymbol> symbolsByOffset = new Dictionary<int, DwarfSymbol>();

        /// <summary>
        /// Initializes a new instance of the <see cref="DwarfCompilationUnit"/> class.
        /// </summary>
        /// <param name="debugData">The debug data stream.</param>
        /// <param name="debugDataDescription">The debug data description stream.</param>
        /// <param name="debugStrings">The debug strings.</param>
        /// <param name="addressNormalizer">Normalize address delegate (<see cref="NormalizeAddressDelegate"/>)</param>
        public DwarfCompilationUnit(DwarfMemoryReader debugData, DwarfMemoryReader debugDataDescription, DwarfMemoryReader debugStrings, NormalizeAddressDelegate addressNormalizer)
        {
            ReadData(debugData, debugDataDescription, debugStrings, addressNormalizer);
        }

        /// <summary>
        /// Gets the symbols tree of all top level symbols defined in this compilation unit.
        /// </summary>
        public DwarfSymbol[] SymbolsTree { get; private set; }

        /// <summary>
        /// Gets all symbols defined in this compilation unit.
        /// </summary>
        public IEnumerable<DwarfSymbol> Symbols
        {
            get
            {
                return symbolsByOffset.Values;
            }
        }

        /// <summary>
        /// Reads the data for this instance.
        /// </summary>
        /// <param name="debugData">The debug data.</param>
        /// <param name="debugDataDescription">The debug data description.</param>
        /// <param name="debugStrings">The debug strings.</param>
        /// <param name="addressNormalizer">Normalize address delegate (<see cref="NormalizeAddressDelegate"/>)</param>
        private void ReadData(DwarfMemoryReader debugData, DwarfMemoryReader debugDataDescription, DwarfMemoryReader debugStrings, NormalizeAddressDelegate addressNormalizer)
        {
            // Read header
            bool is64bit;
            int beginPosition = debugData.Position;
            ulong length = debugData.ReadLength(out is64bit);
            int endPosition = debugData.Position + (int)length;
            ushort version = debugData.ReadUshort();
            int debugDataDescriptionOffset = debugData.ReadOffset(is64bit);
            byte addressSize = debugData.ReadByte();
            DataDescriptionReader dataDescriptionReader = new DataDescriptionReader(debugDataDescription, debugDataDescriptionOffset);

            // Read data
            List<DwarfSymbol> symbols = new List<DwarfSymbol>();
            Stack<DwarfSymbol> parents = new Stack<DwarfSymbol>();

            while (debugData.Position < endPosition)
            {
                int dataPosition = debugData.Position;
                uint code = debugData.LEB128();

                if (code == 0)
                {
                    parents.Pop();
                    continue;
                }

                DataDescription description = dataDescriptionReader.GetDebugDataDescription(code);
                Dictionary<DwarfAttribute, DwarfAttributeValue> attributes = new Dictionary<DwarfAttribute, DwarfAttributeValue>();

                foreach (DataDescriptionAttribute descriptionAttribute in description.Attributes)
                {
                    DwarfAttribute attribute = descriptionAttribute.Attribute;
                    DwarfFormat format = descriptionAttribute.Format;
                    DwarfAttributeValue attributeValue = new DwarfAttributeValue();

                    switch (format)
                    {
                        case DwarfFormat.Address:
                            attributeValue.Type = DwarfAttributeValueType.Address;
                            attributeValue.Value = debugData.ReadUlong(addressSize);
                            break;
                        case DwarfFormat.Block:
                            attributeValue.Type = DwarfAttributeValueType.Block;
                            attributeValue.Value = debugData.ReadBlock(debugData.LEB128());
                            break;
                        case DwarfFormat.Block1:
                            attributeValue.Type = DwarfAttributeValueType.Block;
                            attributeValue.Value = debugData.ReadBlock(debugData.ReadByte());
                            break;
                        case DwarfFormat.Block2:
                            attributeValue.Type = DwarfAttributeValueType.Block;
                            attributeValue.Value = debugData.ReadBlock(debugData.ReadUshort());
                            break;
                        case DwarfFormat.Block4:
                            attributeValue.Type = DwarfAttributeValueType.Block;
                            attributeValue.Value = debugData.ReadBlock(debugData.ReadUint());
                            break;
                        case DwarfFormat.Data1:
                            attributeValue.Type = DwarfAttributeValueType.Constant;
                            attributeValue.Value = (ulong)debugData.ReadByte();
                            break;
                        case DwarfFormat.Data2:
                            attributeValue.Type = DwarfAttributeValueType.Constant;
                            attributeValue.Value = (ulong)debugData.ReadUshort();
                            break;
                        case DwarfFormat.Data4:
                            attributeValue.Type = DwarfAttributeValueType.Constant;
                            attributeValue.Value = (ulong)debugData.ReadUint();
                            break;
                        case DwarfFormat.Data8:
                            attributeValue.Type = DwarfAttributeValueType.Constant;
                            attributeValue.Value = (ulong)debugData.ReadUlong();
                            break;
                        case DwarfFormat.SData:
                            attributeValue.Type = DwarfAttributeValueType.Constant;
                            attributeValue.Value = (ulong)debugData.SLEB128();
                            break;
                        case DwarfFormat.UData:
                            attributeValue.Type = DwarfAttributeValueType.Constant;
                            attributeValue.Value = (ulong)debugData.LEB128();
                            break;
                        case DwarfFormat.String:
                            attributeValue.Type = DwarfAttributeValueType.String;
                            attributeValue.Value = debugData.ReadString();
                            break;
                        case DwarfFormat.Strp:
                            attributeValue.Type = DwarfAttributeValueType.String;
                            attributeValue.Value = debugStrings.ReadString(debugData.ReadOffset(is64bit));
                            break;
                        case DwarfFormat.Flag:
                            attributeValue.Type = DwarfAttributeValueType.Flag;
                            attributeValue.Value = debugData.ReadByte() != 0;
                            break;
                        case DwarfFormat.FlagPresent:
                            attributeValue.Type = DwarfAttributeValueType.Flag;
                            attributeValue.Value = true;
                            break;
                        case DwarfFormat.Ref1:
                            attributeValue.Type = DwarfAttributeValueType.Reference;
                            attributeValue.Value = (ulong)debugData.ReadByte() + (ulong)beginPosition;
                            break;
                        case DwarfFormat.Ref2:
                            attributeValue.Type = DwarfAttributeValueType.Reference;
                            attributeValue.Value = (ulong)debugData.ReadUshort() + (ulong)beginPosition;
                            break;
                        case DwarfFormat.Ref4:
                            attributeValue.Type = DwarfAttributeValueType.Reference;
                            attributeValue.Value = (ulong)debugData.ReadUint() + (ulong)beginPosition;
                            break;
                        case DwarfFormat.Ref8:
                            attributeValue.Type = DwarfAttributeValueType.Reference;
                            attributeValue.Value = (ulong)debugData.ReadUlong() + (ulong)beginPosition;
                            break;
                        case DwarfFormat.RefUData:
                            attributeValue.Type = DwarfAttributeValueType.Reference;
                            attributeValue.Value = (ulong)debugData.LEB128() + (ulong)beginPosition;
                            break;
                        case DwarfFormat.RefAddr:
                            attributeValue.Type = DwarfAttributeValueType.Reference;
                            attributeValue.Value = (ulong)debugData.ReadOffset(is64bit);
                            break;
                        case DwarfFormat.RefSig8:
                            attributeValue.Type = DwarfAttributeValueType.Invalid;
                            debugData.Position += 8;
                            break;
                        case DwarfFormat.ExpressionLocation:
                            attributeValue.Type = DwarfAttributeValueType.ExpressionLocation;
                            attributeValue.Value = debugData.ReadBlock(debugData.LEB128());
                            break;
                        case DwarfFormat.SecOffset:
                            attributeValue.Type = DwarfAttributeValueType.SecOffset;
                            attributeValue.Value = (ulong)debugData.ReadOffset(is64bit);
                            break;
                        default:
                            throw new Exception($"Unsupported DwarfFormat: {format}");
                    }

                    if (attributes.ContainsKey(attribute))
                    {
                        if (attributes[attribute] != attributeValue)
                        {
                            attributes[attribute] = attributeValue;
                        }
                    }
                    else
                    {
                        attributes.Add(attribute, attributeValue);
                    }
                }

                DwarfSymbol symbol = new DwarfSymbol()
                {
                    Tag = description.Tag,
                    Attributes = attributes,
                    Offset = dataPosition,
                };

                symbolsByOffset.Add(symbol.Offset, symbol);

                if (parents.Count > 0)
                {
                    parents.Peek().Children.Add(symbol);
                    symbol.Parent = parents.Peek();
                }
                else
                {
                    symbols.Add(symbol);
                }

                if (description.HasChildren)
                {
                    symbol.Children = new List<DwarfSymbol>();
                    parents.Push(symbol);
                }
            }

            SymbolsTree = symbols.ToArray();

            if (SymbolsTree.Length > 0)
            {
                // Add void type symbol
                DwarfSymbol voidSymbol = new DwarfSymbol()
                {
                    Tag = DwarfTag.BaseType,
                    Offset = -1,
                    Parent = SymbolsTree[0],
                    Attributes = new Dictionary<DwarfAttribute, DwarfAttributeValue>()
                    {
                        { DwarfAttribute.Name, new DwarfAttributeValue() { Type = DwarfAttributeValueType.String, Value = "void" } },
                        { DwarfAttribute.ByteSize, new DwarfAttributeValue() { Type = DwarfAttributeValueType.Constant, Value = (ulong)0 } },
                    },
                };
                if (SymbolsTree[0].Children == null)
                {
                    SymbolsTree[0].Children = new List<DwarfSymbol>();
                }
                SymbolsTree[0].Children.Insert(0, voidSymbol);
                symbolsByOffset.Add(voidSymbol.Offset, voidSymbol);

                // Post process all symbols
                foreach (DwarfSymbol symbol in Symbols)
                {
                    Dictionary<DwarfAttribute, DwarfAttributeValue> attributes = symbol.Attributes as Dictionary<DwarfAttribute, DwarfAttributeValue>;

                    foreach (DwarfAttributeValue value in attributes.Values)
                    {
                        if (value.Type == DwarfAttributeValueType.Reference)
                        {
                            DwarfSymbol reference;

                            if (symbolsByOffset.TryGetValue((int)value.Address, out reference))
                            {
                                value.Type = DwarfAttributeValueType.ResolvedReference;
                                value.Value = reference;
                            }
                        }
                        else if (value.Type == DwarfAttributeValueType.Address)
                        {
                            value.Value = addressNormalizer(value.Address);
                        }
                    }

                    if ((symbol.Tag == DwarfTag.PointerType && !attributes.ContainsKey(DwarfAttribute.Type))
                        || (symbol.Tag == DwarfTag.Typedef && !attributes.ContainsKey(DwarfAttribute.Type)))
                    {
                        attributes.Add(DwarfAttribute.Type, new DwarfAttributeValue()
                        {
                            Type = DwarfAttributeValueType.ResolvedReference,
                            Value = voidSymbol,
                        });
                    }
                }

                // Merge specifications
                foreach (DwarfSymbol symbol in Symbols)
                {
                    Dictionary<DwarfAttribute, DwarfAttributeValue> attributes = symbol.Attributes as Dictionary<DwarfAttribute, DwarfAttributeValue>;
                    DwarfAttributeValue specificationValue;

                    if (attributes.TryGetValue(DwarfAttribute.Specification, out specificationValue) && specificationValue.Type == DwarfAttributeValueType.ResolvedReference)
                    {
                        DwarfSymbol reference = specificationValue.Reference;
                        Dictionary<DwarfAttribute, DwarfAttributeValue> referenceAttributes = reference.Attributes as Dictionary<DwarfAttribute, DwarfAttributeValue>;

                        foreach (KeyValuePair<DwarfAttribute, DwarfAttributeValue> kvp in attributes)
                        {
                            if (kvp.Key != DwarfAttribute.Specification)
                            {
                                referenceAttributes[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Symbol data description
        /// </summary>
        private struct DataDescription
        {
            /// <summary>
            /// Gets or sets the symbol tag.
            /// </summary>
            public DwarfTag Tag { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether symbol has children.
            /// </summary>
            /// <value>
            ///   <c>true</c> if symbol has children; otherwise, <c>false</c>.
            /// </value>
            public bool HasChildren { get; set; }

            /// <summary>
            /// Gets or sets the symbol data description attributes list.
            /// </summary>
            public List<DataDescriptionAttribute> Attributes { get; set; }
        }

        /// <summary>
        /// Symbol data description attribute.
        /// </summary>
        private struct DataDescriptionAttribute
        {
            /// <summary>
            /// Gets or sets the attribute.
            /// </summary>
            public DwarfAttribute Attribute { get; set; }

            /// <summary>
            /// Gets or sets the format.
            /// </summary>
            public DwarfFormat Format { get; set; }
        }

        /// <summary>
        /// Data description reader helper
        /// </summary>
        private class DataDescriptionReader
        {
            /// <summary>
            /// The debug data description stream
            /// </summary>
            DwarfMemoryReader debugDataDescription;

            /// <summary>
            /// The dictionary of already read symbol data descriptions located by code.
            /// </summary>
            Dictionary<uint, DataDescription> readDescriptions;

            /// <summary>
            /// The last read position.
            /// </summary>
            int lastReadPosition;

            /// <summary>
            /// Initializes a new instance of the <see cref="DataDescriptionReader"/> class.
            /// </summary>
            /// <param name="debugDataDescription">The debug data description.</param>
            /// <param name="startingPosition">The starting position.</param>
            public DataDescriptionReader(DwarfMemoryReader debugDataDescription, int startingPosition)
            {
                readDescriptions = new Dictionary<uint, DataDescription>();
                lastReadPosition = startingPosition;
                this.debugDataDescription = debugDataDescription;
            }

            /// <summary>
            /// Gets the debug data description for the specified code.
            /// </summary>
            /// <param name="findCode">The code to be found.</param>
            public DataDescription GetDebugDataDescription(uint findCode)
            {
                DataDescription result;

                if (readDescriptions.TryGetValue(findCode, out result))
                {
                    return result;
                }

                debugDataDescription.Position = lastReadPosition;
                while (!debugDataDescription.IsEnd)
                {
                    uint code = debugDataDescription.LEB128();
                    DwarfTag tag = (DwarfTag)debugDataDescription.LEB128();
                    bool hasChildren = debugDataDescription.ReadByte() != 0;
                    List<DataDescriptionAttribute> attributes = new List<DataDescriptionAttribute>();

                    while (!debugDataDescription.IsEnd)
                    {
                        DwarfAttribute attribute = (DwarfAttribute)debugDataDescription.LEB128();
                        DwarfFormat format = (DwarfFormat)debugDataDescription.LEB128();

                        while (format == DwarfFormat.Indirect)
                        {
                            format = (DwarfFormat)debugDataDescription.LEB128();
                        }

                        if (attribute == DwarfAttribute.None && format == DwarfFormat.None)
                        {
                            break;
                        }

                        attributes.Add(new DataDescriptionAttribute()
                        {
                            Attribute = attribute,
                            Format = format,
                        });
                    }

                    result = new DataDescription()
                    {
                        Tag = tag,
                        HasChildren = hasChildren,
                        Attributes = attributes,
                    };
                    readDescriptions.Add(code, result);
                    if (code == findCode)
                    {
                        lastReadPosition = debugDataDescription.Position;
                        return result;
                    }
                }

                throw new NotImplementedException();
            }
        }
    }
}
