// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Discover built-in field definitions without maintaining a separate list of source files.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MangosDatabaseStudio.App.Services.FieldData
{
    internal static class FieldDataSeedRegistry
    {
        private const string FieldDataNamespace = "MangosDatabaseStudio.App.Services.FieldData.";

        // Discover public static Create*() methods in built-in FieldData source classes.
        // A new field/table is included automatically when its source class follows this
        // convention and returns a FieldDefinitionSeed. Factory and parameterized methods
        // are deliberately excluded.
        public static IReadOnlyList<FieldDefinitionSeed> Discover()
        {
            List<FieldDefinitionSeed> definitions = new();
            IEnumerable<Type> types = typeof(FieldDataSeedRegistry).Assembly.GetTypes()
                .Where(type => type.IsAbstract && type.IsSealed &&
                    type.Namespace?.StartsWith(FieldDataNamespace, StringComparison.Ordinal) is true)
                .OrderBy(type => type.FullName, StringComparer.Ordinal);

            foreach (Type type in types)
            {
                IEnumerable<MethodInfo> methods = type.GetMethods(
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(method => method.Name.StartsWith("Create", StringComparison.Ordinal) &&
                        method.ReturnType == typeof(FieldDefinitionSeed) &&
                        method.GetParameters().Length == 0 &&
                        !method.ContainsGenericParameters)
                    .OrderBy(method => method.Name, StringComparer.Ordinal);

                foreach (MethodInfo method in methods)
                {
                    try
                    {
                        if (method.Invoke(null, null) is not FieldDefinitionSeed definition)
                        {
                            throw new InvalidOperationException(
                                "The built-in field-data source returned no definition: " +
                                type.FullName + "." + method.Name);
                        }

                        definitions.Add(definition);
                    }
                    catch (TargetInvocationException exception)
                    {
                        throw new InvalidOperationException(
                            "Failed to load the built-in field-data source: " +
                            type.FullName + "." + method.Name,
                            exception.InnerException ?? exception);
                    }
                }
            }

            if (definitions.Count == 0)
            {
                throw new InvalidOperationException(
                    "No built-in field-data sources were discovered. Check the FieldData source classes.");
            }

            return definitions.OrderBy(definition => definition.SourceKey, StringComparer.Ordinal).ToArray();
        }
    }
}
