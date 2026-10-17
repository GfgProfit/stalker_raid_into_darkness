using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using XRay.Importer.Sources;
using XRay.Importer.Sources.Mo2;

namespace XRay.Importer.Tests
{
    public sealed class Mo2ConfigurationTests
    {
        private const string CYRILLIC_PROFILE = "1v2(DX11) (v_12gb) 2.5, Все включено (168)";

        [Test]
        public void IniDecodesQtByteArrayWithUtf8()
        {
            string raw = "\"@ByteArray(1v2(DX11) (v_12gb) 2.5, \\xd0\\x92\\xd1\\x81\\xd0\\xb5 \\xd0\\xb2\\xd0\\xba\\xd0\\xbb\\xd1\\x8e\\xd1\\x87\\xd0\\xb5\\xd0\\xbd\\xd0\\xbe (168))\"";
            Assert.AreEqual(CYRILLIC_PROFILE, Mo2IniFile.DecodeValue(raw));
            Assert.AreEqual("D:\\GRA2.5\\Anomaly 1.5.3 up", Mo2IniFile.DecodeValue("@ByteArray(D:\\\\GRA2.5\\\\Anomaly 1.5.3 up)"));
        }

        [Test]
        public void IniDecodesPercentEncodedKeysAndSections()
        {
            Mo2IniFile ini = Mo2IniFile.Parse("[General]\ngameName=STALKER Anomaly\n[PluginPersistance]\nPython%20Proxy\\tryInit=false\n");
            Assert.AreEqual("STALKER Anomaly", ini.GetValue("General", "gameName"));
            Assert.AreEqual("false", ini.GetValue("PluginPersistance", "Python Proxy\\tryInit"));
        }

        [Test]
        public void PortableInstallationUsesDefaultDirectories()
        {
            using (var directory = new TestDirectory())
            {
                var fixture = new Mo2Fixture(directory);
                fixture.WriteIni(CYRILLIC_PROFILE);
                fixture.AddProfile(CYRILLIC_PROFILE, "+ModA");
                fixture.AddProfile("Second", "+ModA");
                Mo2Installation installation = Mo2InstallationReader.Read(fixture.Mo2Root, null, null);
                Assert.IsTrue(installation.IsPortable);
                Assert.AreEqual(Path.Combine(fixture.Mo2Root, "mods"), installation.ModsDirectory);
                Assert.AreEqual(Path.Combine(fixture.Mo2Root, "profiles"), installation.ProfilesDirectory);
                Assert.AreEqual(Path.Combine(fixture.Mo2Root, "overwrite"), installation.OverwriteDirectory);
                Assert.AreEqual(CYRILLIC_PROFILE, installation.SelectedProfile);
                CollectionAssert.AreEquivalent(new[] { CYRILLIC_PROFILE, "Second" }, installation.Profiles);
                CollectionAssert.Contains(installation.SkipFileSuffixes, ".mohidden");
            }
        }

        [Test]
        public void CustomDirectoriesSupportBaseDirPlaceholderAbsoluteAndRelativePaths()
        {
            using (var directory = new TestDirectory())
            {
                var fixture = new Mo2Fixture(directory);
                string externalMods = directory.Combine("external/mods");
                Directory.CreateDirectory(externalMods);
                string baseDirectory = directory.Combine("instance_base");
                Directory.CreateDirectory(Path.Combine(baseDirectory, "my_profiles"));
                fixture.WriteIni("Default", "base_directory=" + baseDirectory.Replace('\\', '/'), "mod_directory=" + externalMods.Replace('\\', '/'), "profiles_directory=%BASE_DIR%/my_profiles", "overwrite_directory=relative_overwrite");
                Mo2Installation installation = Mo2InstallationReader.Read(fixture.Mo2Root, null, null);
                Assert.AreEqual(Path.GetFullPath(baseDirectory), installation.BaseDirectory);
                Assert.AreEqual(Path.GetFullPath(externalMods), installation.ModsDirectory);
                Assert.AreEqual(Path.GetFullPath(Path.Combine(baseDirectory, "my_profiles")), installation.ProfilesDirectory);
                Assert.AreEqual(Path.GetFullPath(Path.Combine(baseDirectory, "relative_overwrite")), installation.OverwriteDirectory);
            }
        }

        [Test]
        public void GlobalInstanceIsFoundInLocalAppData()
        {
            using (var directory = new TestDirectory())
            {
                string appFolder = directory.Combine("app");
                Directory.CreateDirectory(appFolder);
                File.WriteAllText(Path.Combine(appFolder, Mo2InstallationReader.EXECUTABLE_FILE_NAME), string.Empty);
                string instance = directory.Combine("localappdata/ModOrganizer/Anomaly");
                Directory.CreateDirectory(Path.Combine(instance, "profiles/Default"));
                File.WriteAllText(Path.Combine(instance, "profiles/Default/modlist.txt"), "+A\n");
                File.WriteAllText(Path.Combine(instance, Mo2InstallationReader.INI_FILE_NAME), "[General]\ngameName=STALKER Anomaly\nselected_profile=@ByteArray(Default)\n");
                Mo2Installation installation = Mo2InstallationReader.Read(appFolder, null, directory.Combine("localappdata"));
                Assert.IsFalse(installation.IsPortable);
                Assert.AreEqual(Path.GetFullPath(instance), installation.InstanceFolder);
                CollectionAssert.AreEqual(new[] { "Default" }, installation.Profiles);
                CollectionAssert.AreEqual(new[] { "Anomaly" }, Mo2InstallationReader.FindInstances(directory.Combine("localappdata")));
            }
        }

        [Test]
        public void MissingInstallationReportsActionableError()
        {
            using (var directory = new TestDirectory())
            {
                var exception = Assert.Throws<XRaySourceException>(() => Mo2InstallationReader.Read(directory.Root, null, null));
                StringAssert.Contains(Mo2InstallationReader.INI_FILE_NAME, exception.Message);
            }
        }

        [Test]
        public void ModListPriorityFollowsMo2FileOrder()
        {
            Mo2ModList list = Mo2ProfileReader.ParseModList(new List<string> { "# comment", "+TopFix", "-DisabledMod", "+Patch_separator", "*Unmanaged DLC", "+BaseMod", "+TopFix" });
            Assert.AreEqual(5, list.Entries.Count);
            Assert.AreEqual("TopFix", list.Entries[0].Name);
            Assert.AreEqual(4, list.Entries[0].Priority);
            Assert.AreEqual(0, list.Entries[4].Priority);
            Assert.IsTrue(list.Entries[2].IsSeparator);
            Assert.AreEqual(Mo2ModState.Unmanaged, list.Entries[3].State);
            Assert.AreEqual(1, list.Warnings.Count);
            List<Mo2ModEntry> active = list.GetActiveModsByPriorityDescending();
            CollectionAssert.AreEqual(new[] { "TopFix", "BaseMod" }, active.ConvertAll(entry => entry.Name));
        }

        [Test]
        public void ProfilesAreDetectedOnlyWithModList()
        {
            using (var directory = new TestDirectory())
            {
                directory.WriteText("profiles/A/modlist.txt", "+X\n");
                directory.WriteText("profiles/B/settings.ini", "[General]\n");
                directory.WriteText("profiles/C/modlist.txt", "+Y\n");
                CollectionAssert.AreEqual(new[] { "A", "C" }, Mo2ProfileReader.ListProfiles(directory.Combine("profiles")));
            }
        }
    }
}
