using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RaidIntoDarkness.Inventory.Infrastructure.IconGeneration;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Editor
{
    internal sealed class ItemIconCatalogValidationResult
    {
        public readonly List<string> Errors = new();
        public int EntryCount;
        public int MissingVariantCount;
        public bool IsValid => Errors.Count == 0;
    }
}
