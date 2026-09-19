namespace DCFApixels.WhimTex
{
    /// <summary>
    /// A document type that serializes itself instead of being walked by reflection.
    /// <para>
    /// The payload keeps the same shape as the automatic pass — [type name][value count][name][value]… —
    /// so a document stays readable no matter which side wrote it, and a type may switch between the two
    /// modes without a format change. The automatic pass remains the default; this contract exists for the
    /// types where reflection and per-field boxing cost more than the code does, and where migrations must
    /// be explicit rather than implied by field names.
    /// </para>
    /// <para>
    /// Writing must begin with <see cref="IWhimTexDocumentWriter.Begin"/> and the declared count must match
    /// the number of values written; reading iterates <see cref="IWhimTexDocumentReader.Count"/> values and
    /// must consume every one of them, which is what keeps a stream in step with its writer.
    /// </para>
    /// </summary>
    public interface IWhimTexDocumentSerializable
    {
        void WriteDocument(IWhimTexDocumentWriter writer);
        void ReadDocument(IWhimTexDocumentReader reader);
    }

    /// <summary>Writes the fields of a manually serialized type. Values keep the automatic encoding.</summary>
    public interface IWhimTexDocumentWriter
    {
        /// <summary>Declares how many values follow. Call it once, before the first Write.</summary>
        void Begin(int count);

        void Write(string name, int value);
        void Write(string name, long value);
        void Write(string name, float value);
        void Write(string name, double value);
        void Write(string name, bool value);
        void Write(string name, char value);
        void Write(string name, string value);
        void Write(string name, UnityEngine.Vector2 value);
        void Write(string name, UnityEngine.Vector3 value);
        void Write(string name, UnityEngine.Vector4 value);
        void Write(string name, UnityEngine.Vector2Int value);
        void Write(string name, UnityEngine.Vector3Int value);
        void Write(string name, UnityEngine.Quaternion value);
        void Write(string name, UnityEngine.Color value);
        void Write(string name, UnityEngine.Color32 value);
        void Write(string name, UnityEngine.Rect value);
        void Write(string name, UnityEngine.RectInt value);
        void Write(string name, UnityEngine.Bounds value);
        void Write(string name, UnityEngine.AnimationCurve value);
        void WriteEnum<T>(string name, T value) where T : struct, System.Enum;
        /// <summary>Writes a nested object graph; each type decides for itself whether it is manual.</summary>
        void WriteObject(string name, object value, System.Type type);
        void WriteList(string name, System.Collections.IList value, System.Type type);
        void WriteReference(string name, UnityEngine.Object value);
        void WriteTexture(string name, UnityEngine.Texture2D value);
    }

    /// <summary>Reads the fields of a manually serialized type. Values keep the automatic encoding.</summary>
    public interface IWhimTexDocumentReader
    {
        /// <summary>How many values the writer declared. Read once per object.</summary>
        int Count { get; }
        /// <summary>Moves to the next value and returns its name.</summary>
        string NextName();

        int ReadInt();
        long ReadLong();
        float ReadFloat();
        double ReadDouble();
        bool ReadBool();
        char ReadChar();
        string ReadString();
        UnityEngine.Vector2 ReadVector2();
        UnityEngine.Vector3 ReadVector3();
        UnityEngine.Vector4 ReadVector4();
        UnityEngine.Vector2Int ReadVector2Int();
        UnityEngine.Vector3Int ReadVector3Int();
        UnityEngine.Quaternion ReadQuaternion();
        UnityEngine.Color ReadColor();
        UnityEngine.Color32 ReadColor32();
        UnityEngine.Rect ReadRect();
        UnityEngine.RectInt ReadRectInt();
        UnityEngine.Bounds ReadBounds();
        UnityEngine.AnimationCurve ReadCurve();
        T ReadEnum<T>() where T : struct, System.Enum;
        object ReadObject(System.Type type);
        System.Collections.IList ReadList(System.Type type);
        UnityEngine.Object ReadReference();
        UnityEngine.Texture2D ReadTexture();
        /// <summary>Consumes a value this build does not use: never leave one unread.</summary>
        void Skip();
    }
}
