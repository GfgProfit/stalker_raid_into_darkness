using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class XRayParticleGroupReaderTests
    {
        [Test]
        public void PackedGroupPreservesNamedChildLinksTimesFlagsAndEmptyChildren()
        {
            Type reader = Type.GetType("RaidIntoDarkness.Integrations.Inventory.Editor.XRayParticleLibraryReader, RaidIntoDarkness.Integrations.Inventory.Editor", true);
            Type effectType = reader.Assembly.GetType("RaidIntoDarkness.Integrations.Inventory.Editor.XRayParticleEffectData", true);
            Type groupType = reader.Assembly.GetType("RaidIntoDarkness.Integrations.Inventory.Editor.XRayParticleGroupData", true);
            var effects = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(effectType));
            var groups = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(groupType));
            MethodInfo read = reader.GetMethod("Read", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(read);
            read.Invoke(null, new object[] { Library(), new Func<string, bool>(_ => true), effects, groups });
            Assert.That(effects.Count, Is.Zero);
            Assert.That(groups.Count, Is.EqualTo(1));
            Assert.That(Get<string>(groups[0], "Name"), Is.EqualTo("anomaly2\\test_group"));
            Assert.That(Get<float>(groups[0], "TimeLimit"), Is.EqualTo(9.5f));
            var entries = Get<IList>(groups[0], "Effects");
            Assert.That(entries.Count, Is.EqualTo(2));
            Assert.That(Get<string>(entries[0], "EffectName"), Is.EqualTo("anomaly2\\parent"));
            Assert.That(Get<string>(entries[0], "OnPlayChild"), Is.EqualTo("anomaly2\\on_play"));
            Assert.That(Get<string>(entries[0], "OnBirthChild"), Is.EqualTo("anomaly2\\on_birth"));
            Assert.That(Get<string>(entries[0], "OnDeathChild"), Is.EqualTo("anomaly2\\on_death"));
            Assert.That(Get<float>(entries[0], "Time0"), Is.EqualTo(0.125f));
            Assert.That(Get<float>(entries[0], "Time1"), Is.EqualTo(2.75f));
            Assert.That(Get<uint>(entries[0], "Flags"), Is.EqualTo(0x77u));
            Assert.That(Get<string>(entries[1], "EffectName"), Is.EqualTo("anomaly2\\empty"));
            Assert.That(Get<string>(entries[1], "OnPlayChild"), Is.Empty);
            Assert.That(Get<string>(entries[1], "OnBirthChild"), Is.Empty);
            Assert.That(Get<string>(entries[1], "OnDeathChild"), Is.Empty);
            Assert.That(Get<float>(entries[1], "Time0"), Is.Zero);
            Assert.That(Get<float>(entries[1], "Time1"), Is.Zero);
            Assert.That(Get<uint>(entries[1], "Flags"), Is.EqualTo(4u));
        }

        private static T Get<T>(object value, string name)
        {
            PropertyInfo property = value.GetType().GetProperty(name);
            Assert.NotNull(property, name);
            return (T)property.GetValue(value);
        }

        private static byte[] Library()
        {
            byte[] entries = Bytes(writer =>
            {
                writer.Write(2u);
                Entry(writer, "anomaly2\\parent", "anomaly2\\on_play", "anomaly2\\on_birth", "anomaly2\\on_death", 0.125f, 2.75f, 0x77u);
                Entry(writer, "anomaly2\\empty", string.Empty, string.Empty, string.Empty, 0f, 0f, 4u);
            });
            byte[] group = Bytes(writer =>
            {
                Chunk(writer, 2u, Bytes(output => String(output, "anomaly2\\test_group")));
                Chunk(writer, 4u, entries);
                Chunk(writer, 5u, Bytes(output => output.Write(9.5f)));
            });
            return Bytes(writer => Chunk(writer, 4u, Bytes(output => Chunk(output, 0u, group))));
        }

        private static byte[] Bytes(Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
                {
                    write(writer);
                    writer.Flush();
                }
                return stream.ToArray();
            }
        }

        private static void Entry(BinaryWriter writer, string name, string play, string birth, string death, float time0, float time1, uint flags)
        {
            String(writer, name);
            String(writer, play);
            String(writer, birth);
            String(writer, death);
            writer.Write(time0);
            writer.Write(time1);
            writer.Write(flags);
        }

        private static void String(BinaryWriter writer, string value)
        {
            writer.Write(Encoding.ASCII.GetBytes(value));
            writer.Write((byte)0);
        }

        private static void Chunk(BinaryWriter writer, uint id, byte[] bytes)
        {
            writer.Write(id);
            writer.Write((uint)bytes.Length);
            writer.Write(bytes);
        }
    }
}
