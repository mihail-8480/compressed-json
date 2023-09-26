
using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json.Serialization;

[JsonSourceGenerationOptions()]
[JsonSerializable(typeof(JsonElement))]
internal partial class JsonDecoder : JsonSerializerContext
{

}

internal class Program
{
    private static async Task Main(string[] args)
    {
        try
        {
            var mode = GetMode();


            await using var stdin = Console.OpenStandardInput();
            await using var stdout = Console.OpenStandardOutput();
            switch (mode)
            {
                case Mode.Unzip:
                    using (var compressed = Decompress(stdin))
                    {
                        using var tar = new TarReader(compressed);
                        TarEntry? entry = null;
                        var entries = new Dictionary<string, MemoryStream>();
                        do
                        {
                            entry = tar.GetNextEntry();
                            if (entry == null)
                            {
                                break;
                            }
                            Console.WriteLine("Decompressing: {0}", entry.Name);
                            var ms = new MemoryStream();
                            entry.DataStream?.CopyTo(ms);
                            entries.Add(entry.Name, ms);

                        } while (entry != null);

                        var stringStream = entries["__strings__"];
                        stringStream.Seek(0, SeekOrigin.Begin);
                        using var jbin = new JsonBinary(stringStream);

                        foreach (var (name, stream) in entries)
                        {
                            if (name == "__strings__")
                            {
                                continue;
                            }
                            var path = name.EndsWith(".bin") ? name[..^".bin".Length] : name;
                            Console.WriteLine("Decoding: {0}", path);
                            stream.Seek(0, SeekOrigin.Begin);
                            using var input = JsonBinaryFile.FromStream(stream);
                            using var file = File.Open(path, FileMode.Create);
                            jbin.Decode(file, input);
                        }
                    }
                    break;
                case Mode.Zip:
                    var files = args.Skip(1).ToArray();
                    var filesWhereNotExist = files.Where(x => !File.Exists(x)).ToArray();
                    if (filesWhereNotExist.Length > 0)
                    {
                        throw new Exception("The following files were not found: " + Environment.NewLine + filesWhereNotExist.Aggregate((a, b) => a + Environment.NewLine + b));
                    }
                    using (var compressed = Compress(stdout))
                    {
                        using var tar = new TarWriter(compressed);
                        var stringMs = new MemoryStream();
                        using var jbin = new JsonBinary(stringMs);
                        foreach (var file in files)
                        {

                            var tarEntry = new V7TarEntry(TarEntryType.V7RegularFile, file + ".bin");
                            var jsonElement = (JsonElement)JsonSerializer.Deserialize(File.OpenRead(file), typeof(JsonElement), JsonDecoder.Default)!;
                            using var memStream = new MemoryStream();
                            using (var output = JsonBinaryFile.FromStream(memStream))
                                jbin.Encode(jsonElement, output);
                            memStream.Seek(0, SeekOrigin.Begin);
                            tarEntry.DataStream = memStream;
                            tar.WriteEntry(tarEntry);
                        }

                        var stringsEntry = new V7TarEntry(TarEntryType.V7RegularFile, "__strings__");
                        stringMs.Seek(0, SeekOrigin.Begin);
                        stringsEntry.DataStream = stringMs;
                        tar.WriteEntry(stringsEntry);
                    }
                    break;
                case Mode.Encode:
                    using (var output = JsonBinaryFile.FromStream(stdout))
                    {
                        using var jbin = GetJsonBinary();
                        var jsonElement = (JsonElement)JsonSerializer.Deserialize(stdin, typeof(JsonElement), JsonDecoder.Default)!;
                        jbin.Encode(jsonElement, output);
                    }
                    break;
                case Mode.Decode:
                    using (var input = JsonBinaryFile.FromStream(stdin))
                    {
                        using var jbin = GetJsonBinary();
                        jbin.Decode(stdout, input);

                    }
                    break;
            }
        }
        catch (Exception e)
        {
            var error = string.IsNullOrEmpty(e.Message) ? e.GetType().Name.Replace("Exception", "") : e.Message;
            Console.Error.WriteLine("Error: " + error);
        }

        JsonBinary GetJsonBinary()
        {
            var stringStream = File.Open(args.Length == 1 ? "strings.bin" : args[1], FileMode.OpenOrCreate);
            return new JsonBinary(stringStream);
        }

        Mode GetMode()
        {
            if (args.Length == 0)
            {
                throw new InvalidOperationException("No operation");
            }
            return args[0] switch
            {
                "encode" => Mode.Encode,
                "decode" => Mode.Decode,
                "zip" => Mode.Zip,
                "unzip" => Mode.Unzip,
                _ => throw new InvalidOperationException("The first argument must be encode, decode, zip or unzip"),
            };
        }


        Stream Decompress(Stream stream)
        {
            return (Environment.GetEnvironmentVariable("COMPRESSION_ALGORITHM") ?? "brotli") switch
            {
                "deflate" => new DeflateStream(stream, CompressionMode.Decompress),
                "gzip" => new GZipStream(stream, CompressionMode.Decompress),
                _ => new BrotliStream(stream, CompressionMode.Decompress)
            };
        }

        Stream Compress(Stream stream)
        {
            return (Environment.GetEnvironmentVariable("COMPRESSION_ALGORITHM") ?? "brotli") switch
            {
                "deflate" => new DeflateStream(stream, CompressionLevel.SmallestSize),
                "gzip" => new GZipStream(stream, CompressionLevel.SmallestSize),
                _ => new BrotliStream(stream, CompressionLevel.SmallestSize)
            };
        }
    }
}

enum Mode
{
    Encode,
    Decode,
    Zip,
    Unzip
}
