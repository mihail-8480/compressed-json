[StructLayout(LayoutKind.Sequential, Size = 12, Pack = 4)]
public struct Chunk
{
    public static IWriter<T> GetEncoder<T>(Stream stream) where T : unmanaged
    {
        return new Encoder<T>(stream);
    }
    public static IEnumerable<T> GetDecoder<T>(Stream stream) where T : unmanaged
    {
        return new Decoder<T>(stream);
    }
    private class Decoder<T> : IEnumerable<T> where T : unmanaged
    {
        private readonly Stream stream;
        private readonly IEnumerable<T> values;

        public Decoder(Stream stream)
        {
            this.stream = stream;
            this.values = Iterate();
        }


        private T GetFromInt(int i)
        {
            unsafe
            {
                return *(T*)&i;
            }
        }
        private IEnumerable<T> Iterate()
        {
            Chunk? previousChunk = null;
            while (true)
            {
                var chunk = ReadChunk(out int amount);
                var normalizedAmount = amount == 0 ? 32 : amount;
                if (!previousChunk.HasValue)
                {
                    if (chunk == null)
                    {
                        yield break;
                    }
                }
                else
                {
                    var transferChunk = previousChunk.Value;
                    for (int i = 0; i < normalizedAmount; i++)
                    {
                        yield return GetFromInt(transferChunk[i]);
                    }
                }
                previousChunk = chunk;

            }
        }

        private unsafe Chunk? ReadChunk(out int amount)
        {
            var chunkBytes = new byte[sizeof(Chunk)];
            var result = stream.Read(chunkBytes);
            if (result == 0)
            {
                amount = 0;
                return null;
            }
            if (result != sizeof(Chunk))
            {
                if (result != sizeof(byte))
                {
                    throw new InvalidOperationException($"Invalid amount of bytes read: {result}");
                }
                amount = chunkBytes[0];
                return null;
            }
            else
            {
                fixed (byte* b = chunkBytes)
                {
                    amount = 0;
                    return *(Chunk*)b;
                }
            }
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return values.GetEnumerator();
        }

        public IEnumerator GetEnumerator()
        {
            throw new NotImplementedException();
        }
    }

    private class Encoder<T> : IDisposable, IWriter<T> where T : unmanaged
    {
        private readonly Stream stream;
        private byte currentChunkSize;
        private Chunk currentChunk;
        public Encoder(Stream stream)
        {
            currentChunkSize = 0;
            this.stream = stream;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            var size = currentChunkSize;
            if (size > 0)
            {
                FlushCurrentChunk();
                unsafe
                {
                    byte* sizePtr = &size;
                    stream.Write(new Span<byte>(sizePtr, sizeof(byte)));
                }
            }
            stream.Flush();
        }

        private void FlushCurrentChunk()
        {

            unsafe
            {
                fixed (Chunk* chunk = &currentChunk)
                {
                    stream.Write(new Span<byte>(chunk, sizeof(Chunk)));
                }
            }
            currentChunkSize = 0;
            currentChunk = new();
        }

        public void Write(T type)
        {
            if (currentChunkSize == 32)
            {
                FlushCurrentChunk();
            }

            unsafe
            {
                currentChunk[currentChunkSize++] = *(int*)&type;
            }
        }

    }

    BitVector32 vector0 = new();
    BitVector32 vector1 = new();
    BitVector32 vector2 = new();
    public Chunk()
    {
    }


    public int this[int index]
    {
        get => Get(index);
        set => Set(index, value);
    }
    private void Set(int offset, int value)
    {
        if (offset > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Invalid bit offset");
        }

        var val0 = value & 0b001;
        var val1 = value & 0b010;
        var val2 = value & 0b100;
        var mask = BitVector32.CreateMask();
        for (var i = 0; i < offset; i++)
        {
            mask = BitVector32.CreateMask(mask);
        }
        vector0[mask] = val0 > 0;
        vector1[mask] = val1 > 0;
        vector2[mask] = val2 > 0;
    }

    private int Get(int offset)
    {
        if (offset > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Invalid bit offset");
        }

        var mask = BitVector32.CreateMask();
        for (var i = 0; i < offset; i++)
        {
            mask = BitVector32.CreateMask(mask);
        }
        var val0 = vector0[mask] ? 0b001 : 0;
        var val1 = vector1[mask] ? 0b010 : 0;
        var val2 = vector2[mask] ? 0b100 : 0;
        return val0 | val1 | val2;
    }
}