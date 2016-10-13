﻿using CsDebugScript.CodeGen.TypeTrees;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CsDebugScript.CodeGen.UserTypes
{
    /// <summary>
    /// User type that represents static class for getting global variables located in Module.
    /// </summary>
    /// <seealso cref="UserType" />
    internal class GlobalsUserType : UserType
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GlobalsUserType"/> class.
        /// </summary>
        /// <param name="symbol">The symbol we are generating this user type from.</param>
        /// <param name="xmlType">The XML description of the type.</param>
        /// <param name="nameSpace">The namespace it belongs to.</param>
        public GlobalsUserType(Symbol symbol, XmlType xmlType, string nameSpace)
            : base(symbol, xmlType, nameSpace)
        {
        }

        /// <summary>
        /// Gets the class name for this user type. Class name doesn't contain namespace.
        /// </summary>
        public override string OriginalClassName
        {
            get
            {
                return TypeName;
            }
        }

        /// <summary>
        /// Extracts all fields from the user type.
        /// </summary>
        /// <param name="factory">The user type factory.</param>
        /// <param name="generationFlags">The user type generation flags.</param>
        protected override IEnumerable<UserTypeField> ExtractFields(UserTypeFactory factory, UserTypeGenerationFlags generationFlags)
        {
            ExportStaticFields = true;

            var fields = Symbol.Fields.OrderBy(s => s.Name).ToArray();
            bool useThisClass = generationFlags.HasFlag(UserTypeGenerationFlags.UseClassFieldsFromDiaSymbolProvider);
            string previousName = "";

            foreach (var field in fields)
            {
                if (string.IsNullOrEmpty(field.Type.Name))
                    continue;

                if (IsFieldFiltered(field) || field.Name == previousName)
                    continue;

                if (field.Name.Contains("@"))
                {
                    // Skip names contaings '@'
                    continue;
                }

                // Skip fields that are actual values of enum values
                if (field.Type.Tag == Dia2Lib.SymTagEnum.SymTagEnum && field.Type.GetEnumValues().Any(t => t.Item1 == field.Name))
                    continue;

                var userField = ExtractField(field, factory, generationFlags, forceIsStatic: true);

                if (field.Type.Tag == Dia2Lib.SymTagEnum.SymTagPointerType)
                {
                    // Do not use const values for pointers.
                    // We do not allow user type implicit conversion from integers.
                    userField.ConstantValue = string.Empty;
                }

                userField.FieldName = NormalizeSymbolNamespace(userField.FieldName);
                userField.PropertyName = NormalizeSymbolNamespace(userField.PropertyName);

                yield return userField;
                previousName = field.Name;
            }

            foreach (var field in GetAutoGeneratedFields(false, useThisClass))
                    yield return field;
        }


        /// <summary>
        /// Gets the type tree for the base class.
        /// If class has multi inheritance, it can return MultiClassInheritanceTypeTree or SingleClassInheritanceWithInterfacesTypeTree.
        /// </summary>
        /// <param name="error">The error text writer.</param>
        /// <param name="type">The type for which we are getting base class.</param>
        /// <param name="factory">The user type factory.</param>
        /// <param name="baseClassOffset">The base class offset.</param>
        protected override TypeTree GetBaseClassTypeTree(TextWriter error, Symbol type, UserTypeFactory factory, out int baseClassOffset)
        {
            baseClassOffset = 0;
            return new StaticClassTypeTree();
        }

        /// <summary>
        /// Generates the constructors.
        /// </summary>
        /// <param name="generationFlags">The user type generation flags.</param>
        protected override IEnumerable<UserTypeConstructor> GenerateConstructors(UserTypeGenerationFlags generationFlags)
        {
            yield return new UserTypeConstructor()
            {
                ContainsFieldDefinitions = true,
                Static = true,
            };
        }
    }
}
