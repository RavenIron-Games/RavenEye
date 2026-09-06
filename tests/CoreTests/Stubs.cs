// Hand-written stand-ins for the handful of game / BepInEx types the tested source
// mentions. Deliberately minimal: nothing here needs to behave like Valheim beyond what
// the tests assert on — it only needs to compile and let the real logic run.
//
// The one stub that DOES mirror the game is ZPackage: it wraps a BinaryWriter/BinaryReader
// over a MemoryStream with the same primitive encodings the real one uses (decompiled
// 0.221.12: Write(string) → BinaryWriter.Write(string); Write(ZDOID) → long UserID then
// uint ID; Write(Vector3) → three floats), so the packet round-trip test exercises the
// SHIPPING writer against the SHIPPING reader over real bytes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// ---- UnityEngine ------------------------------------------------------------------

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public override string ToString() => $"({x},{y},{z})";
    }
}

// ---- Valheim ----------------------------------------------------------------------
// Global-namespace types in the real game, so the stubs are too.

public struct ZDOID : IEquatable<ZDOID>
{
    public static readonly ZDOID None = new ZDOID(0L, 0u);

    public long UserID { get; }
    public uint ID { get; }

    public ZDOID(long userID, uint id) { UserID = userID; ID = id; }

    public bool IsNone() => UserID == 0L && ID == 0u;

    public bool Equals(ZDOID other) => UserID == other.UserID && ID == other.ID;
    public override bool Equals(object obj) => obj is ZDOID other && Equals(other);
    public override int GetHashCode() => UserID.GetHashCode() ^ (int)ID;
    public static bool operator ==(ZDOID a, ZDOID b) => a.Equals(b);
    public static bool operator !=(ZDOID a, ZDOID b) => !a.Equals(b);
    public override string ToString() => $"{UserID}:{ID}";
}

public class ZPackage
{
    private readonly MemoryStream _stream;
    private readonly BinaryWriter _writer;
    private readonly BinaryReader _reader;

    public ZPackage()
    {
        _stream = new MemoryStream();
        _writer = new BinaryWriter(_stream, Encoding.UTF8);
        _reader = new BinaryReader(_stream, Encoding.UTF8);
    }

    public ZPackage(byte[] data) : this()
    {
        _stream.Write(data, 0, data.Length);
        _stream.Position = 0;
    }

    public byte[] GetArray() { _writer.Flush(); return _stream.ToArray(); }
    public long Size => _stream.Length;

    public void Write(int v)    => _writer.Write(v);
    public void Write(bool v)   => _writer.Write(v);
    public void Write(float v)  => _writer.Write(v);
    public void Write(string v) => _writer.Write(v);
    public void Write(ZDOID id) { _writer.Write(id.UserID); _writer.Write(id.ID); }
    public void Write(UnityEngine.Vector3 v) { _writer.Write(v.x); _writer.Write(v.y); _writer.Write(v.z); }

    public int ReadInt()       => _reader.ReadInt32();
    public bool ReadBool()     => _reader.ReadBoolean();
    public float ReadSingle()  => _reader.ReadSingle();
    public string ReadString() => _reader.ReadString();
    public ZDOID ReadZDOID()   => new ZDOID(_reader.ReadInt64(), _reader.ReadUInt32());
    public UnityEngine.Vector3 ReadVector3()
        => new UnityEngine.Vector3(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle());
}

public class ZNet
{
    public struct PlayerInfo
    {
        public string m_name;
        public ZDOID m_characterID;
        public bool m_publicPosition;
        public UnityEngine.Vector3 m_position;
    }
}

// ---- BepInEx ----------------------------------------------------------------------

namespace BepInEx.Configuration
{
    public class AcceptableValueRange<T>
    {
        public T Min, Max;
        public AcceptableValueRange(T min, T max) { Min = min; Max = max; }
    }

    public class ConfigDescription
    {
        public string Description;
        public object AcceptableValues;
        public ConfigDescription(string description, object acceptableValues = null)
        {
            Description = description;
            AcceptableValues = acceptableValues;
        }
    }

    public class ConfigEntry<T>
    {
        public T Value { get; set; }
        public ConfigDescription Description { get; }
        public ConfigEntry(T value, ConfigDescription description) { Value = value; Description = description; }
    }

    public class ConfigFile
    {
        public readonly List<string> Keys = new List<string>();

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description = null)
        {
            Keys.Add(section + "." + key);
            return new ConfigEntry<T>(defaultValue, new ConfigDescription(description));
        }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, ConfigDescription description)
        {
            Keys.Add(section + "." + key);
            return new ConfigEntry<T>(defaultValue, description);
        }
    }
}
