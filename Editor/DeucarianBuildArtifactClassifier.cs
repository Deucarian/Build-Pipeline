using System;
using System.IO;
using System.IO.Compression;

namespace Deucarian.BuildPipeline
{
    internal static class DeucarianBuildArtifactClassifier
    {
        internal static DeucarianBuildArtifact Classify(string fullPath, string relativePath)
        {
            FileInfo file = new FileInfo(fullPath);
            string normalizedPath = relativePath.Replace('\\', '/');
            string lower = normalizedPath.ToLowerInvariant();
            string encoding = GetEncoding(lower);

            return new DeucarianBuildArtifact
            {
                relativePath = normalizedPath,
                classification = GetClassification(lower),
                encoding = encoding,
                encodedBytes = file.Length,
                rawBytes = GetRawSize(fullPath, encoding, file.Length),
                preEngineBootstrap = !lower.StartsWith("streamingassets/", StringComparison.Ordinal)
            };
        }

        private static string GetEncoding(string lowerPath)
        {
            if (lowerPath.EndsWith(".br", StringComparison.Ordinal))
            {
                return "br";
            }

            if (lowerPath.EndsWith(".gz", StringComparison.Ordinal)
                || lowerPath.EndsWith(".gzip", StringComparison.Ordinal))
            {
                return "gzip";
            }

            return "identity";
        }

        private static string GetClassification(string lowerPath)
        {
            if (lowerPath.Contains(".wasm"))
            {
                return "wasm";
            }

            if (lowerPath.Contains(".data"))
            {
                return "data";
            }

            if (lowerPath.Contains("framework") && lowerPath.Contains(".js"))
            {
                return "framework";
            }

            if (lowerPath.Contains("loader") && lowerPath.Contains(".js"))
            {
                return "loader";
            }

            if (lowerPath.EndsWith(".html") || lowerPath.EndsWith(".htm"))
            {
                return "html";
            }

            if (lowerPath.Contains("symbols") || lowerPath.EndsWith(".map"))
            {
                return "debug-symbols";
            }

            return "support";
        }

        private static long GetRawSize(string fullPath, string encoding, long encodedSize)
        {
            if (encoding == "identity")
            {
                return encodedSize;
            }

            try
            {
                using (FileStream input = File.OpenRead(fullPath))
                using (Stream decoder = encoding == "br"
                           ? (Stream)new BrotliStream(input, CompressionMode.Decompress)
                           : new GZipStream(input, CompressionMode.Decompress))
                {
                    byte[] buffer = new byte[81920];
                    long rawBytes = 0;
                    int read;
                    while ((read = decoder.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        rawBytes += read;
                    }

                    return rawBytes;
                }
            }
            catch (InvalidDataException)
            {
                return encodedSize;
            }
        }
    }
}
