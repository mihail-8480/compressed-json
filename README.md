# Binary Format for JSON

This is a proof of concept for storing JSON as binary data instead of text.

## Building

Install `dotnet-7.0` or newer and run:

```
dotnet publish -r <platform> -c Release
```

## Usage

### Encoding

```
compressed-json encode [strings.bin]
```

Encode a JSON file into a binary file. The input is `stdin`, the output is `stdout` and the common strings are saved (and appended) to `strings.bin` or any second argument provided.

### Decoding

```
compressed-json decode [strings.bin]
```

Decode a previously encoded binary file into a JSON file. The input is `stdin`, the output is `stdout` and the common strings are loaded from `strings.bin` or any second argument provided.

### Compression

```
compressed-json zip [...files]
```

Compress and encode multiple JSON files into a `tar` archive.
The output is `stdout`. The common strings are stored inside of a `tar` entry called `__strings__`.

### Decompression

```
compressed-json unzip
```

Decompress a previously created `tar` archive into JSON files.
The input is `stdin`.

## Available Compression Algorithms

You may specify one of this compression algorithms using the `COMPRESSION_ALGORITHM` environment variable:

- `brotli` (default)
- `gzip`
- `deflate`

## How does it work?

We split the JSON file into 3 sections:

- The structure / types
- The strings / text data
- The value mapping

For example if we had to represent "Hello, World!" in our binary format we would need to:

- Push a string type to the structure section
- Push the string itself in the strings section if not present
- Get the index of the string from the string section and push it to the value mapping section

For `null`, `true` and `false` it's enough to push their type to the structure section.

For `array` and `object` we need to push their type to the the structure section, then push their children (key followed by value in the case of objects) and then push `undefined` to indicate the end of the array/object.

For `numbers` we just need to push the type to the structure section and it's binary value to the value mapping section.

Types are packed as 3-bit values inside of 12-byte blocks, so they are extremely compact, but the same cannot be said for value mappings as they are most of the file size, luckily they are mostly filled with 0s so it's very easy to compress them with regular compression algorithms.

## Effectiveness

As of the initial commit a specific [5MB JSON file](https://microsoftedge.github.io/Demos/json-dummy-data/5MB-min.json) got compacted down to 812764 bytes total (44459 bytes of shared strings).

That original test file compressed with `gzip` is 638402 bytes in size, and if we call the `zip` function on it with `gzip` we get 17043 bytes, with `deflate` we get 17025 bytes, and with `brotli` we get 9530 bytes.

Based on this test (and how it works in general) it should preform well for big or structured data (like API responses or serialized application data).

Before using keep in mind that you should probably use this just for fun or as a reference implementation because it's currently in an early experimental stage and very badly done (no asynchronous IO, definitely not thread safe, worse than questionable performance) and not thoroughly tested on a lot of data, more research and development needs to be done.
