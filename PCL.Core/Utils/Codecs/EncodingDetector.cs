using System;
using System.IO;
using System.Text;

namespace PCL.Core.Utils.Codecs;

public static class EncodingDetector
{
    private const int _SampleSize = 1024;
    private static readonly Encoding _StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly Encoding _Gb2312 = CodePagesEncodingProvider.Instance.GetEncoding(936)!;
    private static readonly Encoding _StrictGb2312 = CodePagesEncodingProvider.Instance.GetEncoding(
        936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)!;

    /// <summary>
    /// 检测流中的文本编码方式（支持 Seek 的流）
    /// </summary>
    /// <param name="stream">输入流，必须支持 Seek</param>
    /// <param name="readFromBegin">是否将流重置到起始点</param>
    /// <returns>检测到的编码，未识别时返回 UTF-8 或系统默认</returns>
    public static Encoding DetectEncoding(Stream stream, bool readFromBegin = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("流必须支持读操作");
        if (!stream.CanSeek)
            throw new ArgumentException("流必须支持 Seek 操作");

        var originalPosition = stream.Position;
        try
        {
            if (readFromBegin) stream.Position = 0;

            Span<byte> sample = stackalloc byte[_SampleSize];
            var count = stream.ReadAtLeast(sample, sample.Length, false);
            var bytes = sample[..count];
            var bom = _DetectByBom(bytes);
            if (bom is not null) return bom;

            var atEnd = count < sample.Length || stream.Position == stream.Length;
            if (_CanDecode(_StrictUtf8, bytes, atEnd)) return Encoding.UTF8;
            return _CanDecode(_StrictGb2312, bytes, atEnd) ? _Gb2312 : Encoding.Default;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    public static Encoding DetectEncoding(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return DetectEncoding(stream);
    }

    private static Encoding? _DetectByBom(ReadOnlySpan<byte> bytes) => bytes switch
    {
        [0xff, 0xfe, 0x00, 0x00, ..] => Encoding.UTF32,
        [0x00, 0x00, 0xfe, 0xff, ..] => Encoding.GetEncoding(12001),
        [0xef, 0xbb, 0xbf, ..] => Encoding.UTF8,
        [0xfe, 0xff, ..] => Encoding.BigEndianUnicode,
        [0xff, 0xfe, ..] => Encoding.Unicode,
        _ => null
    };

    private static bool _CanDecode(Encoding encoding, ReadOnlySpan<byte> sample, bool atEnd)
    {
        try
        {
            // Keep a partial character at the end of the sample when the stream has more bytes.
            _ = encoding.GetDecoder().GetCharCount(sample, flush: atEnd);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
