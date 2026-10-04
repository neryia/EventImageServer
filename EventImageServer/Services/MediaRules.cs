namespace EventImageServer.Services
{
    // Allow-list and content (magic-number) checks for owner uploads. Anything
    // that is not a known photo/video type is rejected, and the file's leading
    // bytes must match the signature of the type its extension claims, so a
    // script or HTML document renamed to ".jpg" is not accepted either.
    public static class MediaRules
    {
        public const int HeaderLength = 12;

        public static readonly HashSet<string> Photo = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic"
        };

        public static readonly HashSet<string> Video = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".webm", ".ogg", ".mov"
        };

        // Returns "image", "video" or null when the extension is not allowed.
        public static string? Classify(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            var extension = Path.GetExtension(fileName);
            return Photo.Contains(extension) ? "image"
                : Video.Contains(extension) ? "video"
                : null;
        }

        public static bool LooksLikeMedia(string fileName, Stream stream)
        {
            Span<byte> header = stackalloc byte[HeaderLength];
            var read = 0;
            while (read < HeaderLength)
            {
                var n = stream.Read(header[read..]);
                if (n == 0)
                {
                    break;
                }
                read += n;
            }

            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            return SignatureMatches(Path.GetExtension(fileName), header[..read]);
        }

        public static bool SignatureMatches(string? extension, ReadOnlySpan<byte> h)
        {
            if (h.Length < HeaderLength)
            {
                return false;
            }

            return extension?.ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF,
                ".png" => h[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
                ".gif" => h[..6].SequenceEqual("GIF87a"u8) || h[..6].SequenceEqual("GIF89a"u8),
                ".webp" => h[..4].SequenceEqual("RIFF"u8) && h[8..12].SequenceEqual("WEBP"u8),
                ".heic" or ".mp4" or ".mov" => h[4..8].SequenceEqual("ftyp"u8),
                ".webm" => h[0] == 0x1A && h[1] == 0x45 && h[2] == 0xDF && h[3] == 0xA3,
                ".ogg" => h[..4].SequenceEqual("OggS"u8),
                _ => false
            };
        }
    }
}
