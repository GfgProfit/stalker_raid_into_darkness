using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RaidIntoDarkness.Inventory.IconGeneration;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Editor
{
    internal sealed class ItemIconBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            ItemIconCatalogValidationResult validation = ItemIconCatalogValidator.Validate(false);

            if (validation.IsValid == false)
            {
                Debug.LogWarning($"Baked item icon catalog has {validation.Errors.Count} issue(s); stale icons are regenerated at runtime. Open Tools/Inventory/Item Icon Generator to rebake.\n{string.Join("\n", validation.Errors)}");
            }
        }
    }
}
