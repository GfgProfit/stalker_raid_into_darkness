using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RaidIntoDarkness.FirstPerson.Editor
{
    public static class FirstPersonReportWriter
    {
        private const int MAX_LISTED_WARNINGS = 100;

        public static string OutfitsCsv(FirstPersonImportReportData report)
        {
            var builder = new StringBuilder();
            Line(builder, "section", "display_name", "hud_section", "hands_visual", "hands_asset", "hands_source", "legs_section", "body_visual", "body_asset", "body_source", "is_exo", "repair_type", "resolution");
            foreach (FirstPersonOutfitRow row in report.outfits)
            {
                Line(builder, row.section, row.displayName, row.hudSection, row.handsVirtualPath, row.handsAsset, row.handsSource, row.legsSection, row.bodyVirtualPath, row.bodyAsset, row.bodySource, row.isExoskeleton ? "true" : "false", row.repairType, row.resolution);
            }

            return builder.ToString();
        }

        public static string ModelsCsv(FirstPersonImportReportData report)
        {
            var builder = new StringBuilder();
            Line(builder, "kind", "virtual_path", "asset_path", "source", "overridden_sources", "copy", "used_by", "imported", "bones", "skinned_renderers", "vertices", "triangles", "bindpose_mismatches", "materials", "generated_materials", "fallback_materials", "no_base_color", "textures_resolved", "textures_missing", "embedded_motions_in_file", "embedded_motions_imported", "motion_refs", "problems");
            foreach (FirstPersonModelRow row in report.models)
            {
                Line(builder, row.kind, row.virtualPath, row.assetPath, row.source, row.overriddenSources, row.copyStatus, Number(row.usedByOutfits), row.imported ? "true" : "false", Number(row.bones), Number(row.skinnedRenderers), Number(row.vertices), Number(row.triangles), Number(row.bindposeMismatches), Number(row.materials), Number(row.generatedMaterials), Number(row.fallbackMaterials), Number(row.materialsWithoutBaseColor), Number(row.texturesResolved), Number(row.texturesMissing), row.embeddedMotionsPresent ? "true" : "false", row.embeddedMotionsImported ? "true" : "false", row.motionReferences, row.problems);
            }

            return builder.ToString();
        }

        public static string Markdown(FirstPersonImportReportData report)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# First Person Import Report");
            builder.AppendLine();
            builder.AppendLine($"Created (UTC): {report.createdUtc}  ");
            builder.AppendLine($"MO2 profile: {report.profile}, active mods: {report.activeMods}, indexed files: {report.indexedFiles}  ");
            builder.AppendLine($"LTX sections: {report.ltxSections}, DLTX files: {report.dltxFiles}, rus strings: {report.rusStrings}  ");
            builder.AppendLine($"swm_legs script: {report.swmScriptSource}, use_hd_models: {report.useHdModels}");
            builder.AppendLine();
            builder.AppendLine("## Counts");
            builder.AppendLine();
            builder.AppendLine("| Item | Value |");
            builder.AppendLine("|---|---|");
            Row(builder, "Catalog entries (single player outfits + actor without outfit)", report.outfitRows);
            Row(builder, "Single player outfits", report.singlePlayerOutfits);
            Row(builder, "Multiplayer outfits excluded (configs/mp)", report.multiplayerExcluded);
            Row(builder, "Exoskeleton outfits (repair_type = outfit_exo)", report.exoskeletonOutfits);
            Row(builder, "Unique hands models / imported", report.uniqueHands + " / " + report.handsImported);
            Row(builder, "Unique body models / imported", report.uniqueBodies + " / " + report.bodiesImported);
            Row(builder, "Body OMF imported", report.animationsImported);
            Row(builder, "AnimationClips in body OMF", report.clipsInOmf);
            Row(builder, "Motion names present in both OMF", report.duplicateMotionsAcrossOmf);
            Row(builder, "Library bindings / missing clips", report.tableEntries + " / " + report.tableEntriesMissing);
            Row(builder, "Import errors / warnings logged", report.importErrors + " / " + report.importWarnings);
            Row(builder, "Camera height", report.cameraHeight.ToString("0.###", CultureInfo.InvariantCulture) + " (" + report.cameraHeightOrigin + ")");
            Row(builder, "Body offset (swm_legs/legs_offset)", report.legsOffset.ToString("0.###", CultureInfo.InvariantCulture));
            builder.AppendLine();
            builder.AppendLine("Resolution: " + string.Join(", ", report.resolutionCounts));
            builder.AppendLine();
            builder.AppendLine("Excluded multiplayer sections: " + report.multiplayerSections);
            builder.AppendLine();

            List<FirstPersonModelRow> problems = report.models.Where(row => !string.IsNullOrEmpty(row.problems)).ToList();
            builder.AppendLine("## Model problems");
            builder.AppendLine();
            builder.AppendLine(problems.Count == 0 ? "None." : string.Join("\n", problems.Select(row => $"- `{row.assetPath}`: {row.problems}")));
            builder.AppendLine();

            builder.AppendLine("## Skeleton compatibility (body vs body OMF)");
            builder.AppendLine();
            int compatible = report.compatibility.Count(row => row.animatedBonesMissingInBody == 0 && row.curvePathsMissing == 0);
            builder.AppendLine($"Fully compatible bodies: {compatible} / {report.compatibility.Count}");
            builder.AppendLine();
            foreach (IGrouping<string, FirstPersonCompatibilityRow> group in report.compatibility.GroupBy(row => $"{row.bodyBones} bones, missing animated {row.animatedBonesMissingInBody}, not animated {row.bodyBonesNotAnimated}, missing curve paths {row.curvePathsMissing}"))
            {
                builder.AppendLine($"- {group.Key}: {group.Count()} bodies");
                FirstPersonCompatibilityRow first = group.First();
                if (first.missingBoneNames.Length > 0)
                {
                    builder.AppendLine($"  - missing: {first.missingBoneNames}");
                }

                if (first.extraBoneNames.Length > 0)
                {
                    builder.AppendLine($"  - not animated: {first.extraBoneNames}");
                }
            }

            builder.AppendLine();
            builder.AppendLine("## SampleAnimation");
            builder.AppendLine();
            builder.AppendLine("| Body | Motion | Time | Driven | Non-finite | Max rot from rest | Max pos from rest | Max rot diff to first body |");
            builder.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (FirstPersonSampleRow row in report.samples)
            {
                builder.AppendLine($"| {row.body} | {row.motion} | {F(row.time)} | {row.drivenTransforms} | {row.nonFiniteTransforms} | {F(row.maxRotationFromRestDegrees)} | {F(row.maxPositionFromRest)} | {F(row.maxRotationDifferenceToReferenceDegrees)} |");
            }

            builder.AppendLine();
            builder.AppendLine("## Missing body motions");
            builder.AppendLine();
            List<FirstPersonMotionRow> missing = report.motions.Where(row => !row.found).ToList();
            builder.AppendLine(missing.Count == 0 ? "None." : string.Join("\n", missing.Select(row => $"- {row.table}[{row.flags}] `{row.motion}`")));
            builder.AppendLine();
            AppendList(builder, "Errors", report.errors);
            AppendList(builder, "Warnings", report.warnings);
            return builder.ToString();
        }

        private static void AppendList(StringBuilder builder, string title, List<string> items)
        {
            builder.AppendLine($"## {title} ({items.Count})");
            builder.AppendLine();
            foreach (string item in items.Take(MAX_LISTED_WARNINGS))
            {
                builder.AppendLine("- " + item.Replace('\n', ' '));
            }

            if (items.Count > MAX_LISTED_WARNINGS)
            {
                builder.AppendLine($"- ... {items.Count - MAX_LISTED_WARNINGS} more in FirstPersonImportReport.json");
            }

            builder.AppendLine();
        }

        private static void Row(StringBuilder builder, string name, object value)
        {
            builder.AppendLine($"| {name} | {value} |");
        }

        private static string F(float value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static string Number(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static void Line(StringBuilder builder, params string[] values)
        {
            builder.AppendLine(string.Join(",", values.Select(Escape)));
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }
    }
}
