
public class JsonBinaryFile : IDisposable
{
    public required Stream DataMap { get; set; }
    public required Stream StructureMap { get; set; }
    public static JsonBinaryFile FromStream(Stream stream)
    {
        var sectionMaster = new SectionMaster(stream);
        return new JsonBinaryFile
        {
            DataMap = sectionMaster.CreateSection(SectionType.DataMap),
            StructureMap = sectionMaster.CreateSection(SectionType.StructureMap),
            disposable = sectionMaster
        };
    }
    private IDisposable? disposable = null;
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        disposable?.Dispose();
    }

    private struct SectionMetadata
    {
        public long SectionLength;
    }
    private enum SectionType
    {
        DataMap = 0,
        StructureMap = 1
    }
    private unsafe struct SectionFileHeader
    {
        public SectionMetadata DataMapSection;

        public SectionMetadata StructureMapSection;
    }
    private class SectionMaster : IDisposable
    {
        private readonly Dictionary<SectionType, (MemoryStream, MemoryStream)> sectionCache;
        private readonly Stream baseStream;
        private void PopulateSection(SectionType type, SectionMetadata metadata)
        {
            var arr = new byte[metadata.SectionLength];
            baseStream.ReadAtLeast(arr, (int)metadata.SectionLength, true);
            var readMs = new MemoryStream(arr);
            readMs.Seek(0, SeekOrigin.Begin);
            var writeMs = new MemoryStream();
            sectionCache.Add(type, (readMs, writeMs));
        }
        public SectionMaster(Stream baseStream)
        {
            this.baseStream = baseStream;
            this.sectionCache = new Dictionary<SectionType, (MemoryStream, MemoryStream)>();

            var header = new SectionFileHeader();

            unsafe
            {
                var headerPtr = &header;
                var headerBytes = new Span<byte>(headerPtr, sizeof(SectionFileHeader));
                if (!this.baseStream.CanRead || baseStream.Read(headerBytes) != sizeof(SectionFileHeader))
                {
                    PopulateSection(SectionType.DataMap, new SectionMetadata
                    {
                        SectionLength = 0,
                    });
                    PopulateSection(SectionType.StructureMap, new SectionMetadata
                    {
                        SectionLength = 0,
                    });

                    return;
                }
            }

            PopulateSection(SectionType.DataMap, header.DataMapSection);
            PopulateSection(SectionType.StructureMap, header.StructureMapSection);
        }

        private bool dirty = false;

        public void WriteSection(SectionType sectionId, byte[] buffer, int offset, int count)
        {
            dirty = true;
            this.sectionCache[sectionId].Item2.Write(buffer, offset, count);
        }
        public int ReadSection(SectionType sectionId, byte[] buffer, int offset, int count)
        {
            return this.sectionCache[sectionId].Item1.Read(buffer, offset, count);
        }

        private byte[] GetBytesForSection(SectionType type)
        {
            return sectionCache[type].Item2.ToArray();
        }
        public unsafe void FlushSection()
        {
            var dataMapSection = GetBytesForSection(SectionType.DataMap);
            var structureMapSection = GetBytesForSection(SectionType.StructureMap);
            var header = new SectionFileHeader()
            {
                DataMapSection = new SectionMetadata()
                {
                    SectionLength = dataMapSection.LongLength,
                },
                StructureMapSection = new SectionMetadata()
                {
                    SectionLength = structureMapSection.LongLength,
                }
            };
            var headerBytes = new Span<byte>(&header, sizeof(SectionFileHeader));
            baseStream.Write(headerBytes);
            baseStream.Write(dataMapSection);
            baseStream.Write(structureMapSection);
            baseStream.Flush();
        }

        public Stream CreateSection(SectionType sectionId)
        {
            return new SectionStream(this, sectionId);
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            if (dirty)
            {
                FlushSection();
            }
            foreach (var item in sectionCache)
            {

                item.Value.Item1.Dispose();
                item.Value.Item2.Dispose();
            }
        }
    }
    private class SectionStream : Stream
    {
        private readonly SectionMaster master;
        private readonly SectionType sectionId;

        public SectionStream(SectionMaster master, SectionType sectionId)
        {
            this.master = master;
            this.sectionId = sectionId;
        }
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override ValueTask DisposeAsync()
        {
            return base.DisposeAsync();
        }
        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return master.ReadSection(sectionId, buffer, offset, count);
        }


        public override void Write(byte[] buffer, int offset, int count)
        {
            master.WriteSection(sectionId, buffer, offset, count);
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

    }
}

