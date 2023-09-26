
public static class JsonBinaryStrings
{
    public static List<string> Decode(Stream stream)
    {
        var list = new List<string>();
        var builder = new List<byte>();
        while (true)
        {
            var c = stream.ReadByte();
            if (c == -1)
            {
                break;
            }

            if (c == 0)
            {
                var str = Encoding.UTF8.GetString(builder.ToArray());
                list.Add(str);
                builder.Clear();
            }
            else
            {
                builder.Add((byte)c);

            }
        }

        return list;
    }
}