using RaidIntoDarkness.Inventory.Domain;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using RaidIntoDarkness.Inventory.Infrastructure.IconGeneration;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Editor
{
    internal sealed class ItemDefinitionIdValidationResult
    {
        public readonly List<string> Errors = new();
        public readonly List<ItemDefinition> MissingIds = new();
        public readonly List<ItemDefinition> MalformedIds = new();
        public readonly Dictionary<string, List<ItemDefinition>> Duplicates = new(StringComparer.Ordinal);
        public int ValidCount;
        public bool IsValid => Errors.Count == 0;
    }
}
