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
        public void UnsupportedAndDampingPayloadsRetainOriginalOrderFlagsAndBytes()
        {
            byte[] scatter = Bytes(writer =>
            {
                for (int index = 0; index < 6; index++) writer.Write(index * 0.25f);
            });
            byte[] damping = Bytes(writer =>
            {
                for (int index = 0; index < 5; index++) writer.Write(index + 0.5f);
            });
            byte[] actions = Bytes(writer =>
            {
                writer.Write(3u);
                writer.Write(31u);
                writer.Write(2u);
                writer.Write(0u);
                writer.Write(scatter);
                writer.Write(4u);
                writer.Write(6u);
                writer.Write(0u);
                writer.Write(damping);
                writer.Write(12u);
                writer.Write(0u);
                writer.Write(0u);
            });
            IList effects = ReadEffects(EffectLibrary(actions));
            Assert.That(effects.Count, Is.EqualTo(1));
            IList payloads = Get<IList>(effects[0], "UnsupportedPayloads");
            Assert.That(payloads.Count, Is.EqualTo(2));
            Assert.That(Get<int>(payloads[0], "Ordinal"), Is.Zero);
            Assert.That(Get<object>(payloads[0], "Type").ToString(), Is.EqualTo("Scatter"));
            Assert.That(Get<uint>(payloads[0], "Flags"), Is.EqualTo(2u));
            Assert.That(Get<byte[]>(payloads[0], "Bytes"), Is.EqualTo(scatter));
            Assert.That(Get<int>(payloads[1], "Ordinal"), Is.EqualTo(1));
            Assert.That(Get<object>(payloads[1], "Type").ToString(), Is.EqualTo("Damping"));
            Assert.That(Get<uint>(payloads[1], "Flags"), Is.EqualTo(6u));
            Assert.That(Get<byte[]>(payloads[1], "Bytes"), Is.EqualTo(damping));
            Assert.That(Get<IList>(effects[0], "Actions").Count, Is.EqualTo(2));
        }

        [Test]
        public void TruncatedUnsupportedPayloadIsRejectedBeforeReadingAnotherChunk()
        {
            byte[] actions = Bytes(writer =>
            {
                writer.Write(1u);
                writer.Write(31u);
                writer.Write(2u);
                writer.Write(0u);
                writer.Write(new byte[20]);
            });
            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() => ReadEffects(EffectLibrary(actions)));
            Assert.That(exception.InnerException, Is.TypeOf<FormatException>());
        }

        [Test]
        public void PackedGroupPreservesNamedChildLinksTimesFlagsAndEmptyChildren()
        {
            Type reader = Type.GetType("RaidIntoDarkness.Inventory.Editor.XRayParticleLibraryReader, RaidIntoDarkness.Inventory.Editor", true);
            Type effectType = reader.Assembly.GetType("RaidIntoDarkness.Inventory.Editor.XRayParticleEffectData", true);
            Type groupType = reader.Assembly.GetType("RaidIntoDarkness.Inventory.Editor.XRayParticleGroupData", true);
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

        private static IList ReadEffects(byte[] bytes)
        {
            Type reader = Type.GetType("RaidIntoDarkness.Inventory.Editor.XRayParticleLibraryReader, RaidIntoDarkness.Inventory.Editor", true);
            Type effectType = reader.Assembly.GetType("RaidIntoDarkness.Inventory.Editor.XRayParticleEffectData", true);
            Type groupType = reader.Assembly.GetType("RaidIntoDarkness.Inventory.Editor.XRayParticleGroupData", true);
            var effects = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(effectType));
            var groups = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(groupType));
            reader.GetMethod("Read", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { bytes, new Func<string, bool>(_ => true), effects, groups });
            return effects;
        }

        private static byte[] EffectLibrary(byte[] actions)
        {
            byte[] effect = Bytes(writer =>
            {
                Chunk(writer, 2u, Bytes(output => String(output, "anomaly2\\payload_test")));
                Chunk(writer, 3u, Bytes(output => output.Write(10u)));
                Chunk(writer, 4u, actions);
                Chunk(writer, 5u, Bytes(output => output.Write(0u)));
            });
            return Bytes(writer => Chunk(writer, 3u, Bytes(output => Chunk(output, 0u, effect))));
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
