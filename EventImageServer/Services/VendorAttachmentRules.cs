namespace EventImageServer.Services
{
    // Allow-list + signature checks for vendor attachments (contracts,
    // quotes, invoices). Files are served statically, so anything that could
    // execute in the browser (html, svg, js) is rejected.
    public static class VendorAttachmentRules
    {
        public const long MaxBytes = 15 * 1024 * 1024;

        private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".jpg", ".jpeg", ".png", ".webp", ".doc", ".docx"
        };

        public static bool IsAllowedExtension(string? fileName) =>
            !string.IsNullOrWhiteSpace(fileName) && Allowed.Contains(Path.GetExtension(fileName));

        public static bool LooksValid(string fileName, Stream stream)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            Span<byte> header = stackalloc byte[MediaRules.HeaderLength];
            var read = 0;
            while (read < header.Length)
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

            var h = header[..read];
            return extension switch
            {
                ".pdf" => h.Length >= 4 && h[..4].SequenceEqual("%PDF"u8),
                ".docx" => h.Length >= 4 && h[0] == 0x50 && h[1] == 0x4B && h[2] == 0x03 && h[3] == 0x04,
                ".doc" => h.Length >= 8 && h[..8].SequenceEqual(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }),
                _ => MediaRules.SignatureMatches(extension, h)
            };
        }
    }
}
