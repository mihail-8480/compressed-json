
using System.Text.Json.Serialization;


[JsonSourceGenerationOptions()]
[JsonSerializable(typeof(double))]
internal partial class DoubleEncoder : JsonSerializerContext
{

}

[JsonSourceGenerationOptions()]
[JsonSerializable(typeof(string))]
internal partial class StringEncoder : JsonSerializerContext
{

}


public class JsonBinary : IDisposable
{
    private class DecodeContext : IDisposable
    {
        private readonly JsonValueKind[] types;
        private readonly TextWriter writer;
        private readonly Stream values;
        private unsafe T Consume<T>() where T : unmanaged
        {
            T* value = stackalloc T[1];

            if (values.Read(new Span<byte>(value, sizeof(T))) != sizeof(T))
            {
                throw new InvalidOperationException("Corrupt file");
            }
            return *value;

        }
        public DecodeContext(JsonBinaryFile value, Stream dest)
        {
            this.writer = new StreamWriter(dest, Encoding.UTF8, leaveOpen: true);
            this.types = Chunk.GetDecoder<JsonValueKind>(value.StructureMap).ToArray();
            this.values = value.DataMap;
        }

        public void Append(string str)
        {
            writer.Write(str);
        }

        public IEnumerable<(JsonValueKind, ulong)> Decode()
        {
            foreach (var type in types)
            {
                switch (type)
                {
                    case JsonValueKind.String:
                        yield return (type, (ulong)Consume<int>());
                        break;
                    case JsonValueKind.Number:
                        yield return (type, Consume<ulong>());
                        break;
                    case JsonValueKind.Object:
                    case JsonValueKind.Array:
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                    case JsonValueKind.Null:
                    case JsonValueKind.Undefined:
                        yield return (type, 0);
                        break;
                }
            }
        }

        public void Dispose()
        {
            writer.Flush();
            writer.Dispose();
        }
    }
    private class EncodeContext : IDisposable
    {
        private readonly Stream dataMap;
        private readonly IWriter<JsonValueKind> structureWriter;
        public EncodeContext(JsonBinaryFile file)
        {
            this.dataMap = file.DataMap;
            structureWriter = Chunk.GetEncoder<JsonValueKind>(file.StructureMap);
        }


        private void WriteDataSegment<T>(T segment) where T : unmanaged
        {
            unsafe
            {
                var segmentBytes = &segment;
                dataMap.Write(new Span<byte>(segmentBytes, sizeof(T)));
            }
        }

        public void PushEmptyEntry(JsonValueKind kind)
        {
            structureWriter.Write(kind);
        }
        public void PushEntry<T>(JsonValueKind kind, T segment) where T : unmanaged
        {
            structureWriter.Write(kind);
            WriteDataSegment(segment);
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            structureWriter.Dispose();
        }
    }

    private readonly List<string> strings;
    private readonly Stream strStream;
    public JsonBinary(Stream strStream)
    {
        this.strings = JsonBinaryStrings.Decode(strStream);
        this.strStream = strStream;
    }

    private string LookupStringSegment(int segment)
    {
        return strings[segment];
    }
    private int AllocateStringSegment(string str)
    {
        if (strings.Contains(str))
        {
            return strings.IndexOf(str);
        }
        var utf8 = Encoding.UTF8.GetBytes(str);
        strStream.Write(utf8);
        strStream.WriteByte(0);
        strings.Add(str);
        return strings.Count - 1;
    }
    private void EncodeSingle(JsonElement element, EncodeContext context)
    {
        switch (element.ValueKind)
        {

            case JsonValueKind.False:
                context.PushEmptyEntry(JsonValueKind.False);
                break;
            case JsonValueKind.True:
                context.PushEmptyEntry(JsonValueKind.True);
                break;
            case JsonValueKind.Null:
                context.PushEmptyEntry(JsonValueKind.Null);
                break;
            case JsonValueKind.Undefined:
                context.PushEmptyEntry(JsonValueKind.Undefined);
                break;
            case JsonValueKind.Array:
                context.PushEmptyEntry(JsonValueKind.Array);
                foreach (var el in element.EnumerateArray())
                {
                    EncodeSingle(el, context);
                }
                context.PushEmptyEntry(JsonValueKind.Undefined);
                break;
            case JsonValueKind.Object:
                var objArr = element.EnumerateObject().ToArray();
                context.PushEmptyEntry(JsonValueKind.Object);
                foreach (var obj in objArr)
                {
                    context.PushEntry(JsonValueKind.String, AllocateStringSegment(obj.Name));
                    EncodeSingle(obj.Value, context);
                }
                context.PushEmptyEntry(JsonValueKind.Undefined);
                break;
            case JsonValueKind.String:
                context.PushEntry(JsonValueKind.String, AllocateStringSegment(element.GetString() ?? ""));
                break;
            case JsonValueKind.Number:
                unsafe
                {
                    var num = element.GetDouble();
                    context.PushEntry(JsonValueKind.Number, *(ulong*)&num);
                }
                break;

        }
    }

    private void DecodeTree(IEnumerator<(JsonValueKind, ulong)> enumerator, DecodeContext context)
    {
        var (type, value) = enumerator.Current;

        switch (type)
        {
            case JsonValueKind.Undefined:
                throw new InvalidOperationException("Corrupt file");
            case JsonValueKind.Object:
                context.Append("{");
                if (!enumerator.MoveNext())
                {
                    throw new InvalidOperationException("Unexpected end of file");
                }
                for (var i = 0; enumerator.Current.Item1 != JsonValueKind.Undefined; i++)
                {
                    if (i != 0)
                    {
                        context.Append(",");
                    }
                    DecodeTree(enumerator, context);
                    context.Append(":");
                    if (!enumerator.MoveNext())
                    {
                        throw new InvalidOperationException("Unexpected end of file");
                    }
                    DecodeTree(enumerator, context);

                    if (!enumerator.MoveNext())
                    {
                        throw new InvalidOperationException("Unexpected end of file");
                    }
                }
                context.Append("}");

                break;
            case JsonValueKind.Array:
                context.Append("[");
                if (!enumerator.MoveNext())
                {
                    throw new InvalidOperationException("Unexpected end of file");
                }
                for (var i = 0; enumerator.Current.Item1 != JsonValueKind.Undefined; i++)
                {
                    if (i != 0)
                    {
                        context.Append(",");
                    }
                    DecodeTree(enumerator, context);

                    if (!enumerator.MoveNext())
                    {
                        throw new InvalidOperationException("Unexpected end of file");
                    }
                }
                context.Append("]");
                break;
            case JsonValueKind.String:
                context.Append(JsonSerializer.Serialize(LookupStringSegment((int)value), typeof(string), StringEncoder.Default));
                break;
            case JsonValueKind.Number:
                unsafe
                {
                    context.Append(JsonSerializer.Serialize(*(double*)&value, typeof(double), DoubleEncoder.Default));
                }
                break;
            case JsonValueKind.True:
                context.Append("true");
                break;
            case JsonValueKind.False:
                context.Append("false");
                break;
            case JsonValueKind.Null:
                context.Append("null");
                break;
        }
    }

    public void Decode(Stream dest, JsonBinaryFile file)
    {
        using var context = new DecodeContext(file, dest);

        var decoder = context.Decode().GetEnumerator();
        while (decoder.MoveNext())
        {
            DecodeTree(decoder, context);
            context.Append(Environment.NewLine);
        }
    }
    public void Encode(JsonElement element, JsonBinaryFile file)
    {
        using var context = new EncodeContext(file);
        EncodeSingle(element, context);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        strStream.Dispose();
    }
}