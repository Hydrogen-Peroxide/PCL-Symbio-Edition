using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Utils.Codecs;

namespace PCL.Core.Test;
[TestClass]
public class EncodingDetectorTest
{
    [TestMethod]
    public void TestEncoding()
    {
        var utf8 = Encoding.UTF8.GetBytes("Hi, There!");
        Assert.AreEqual(Encoding.UTF8, EncodingDetector.DetectEncoding(utf8));
        utf8 = Encoding.UTF8.GetBytes("棍斤拷烫烫烫");
        Assert.AreEqual(Encoding.UTF8, EncodingDetector.DetectEncoding(utf8));
        var gb2312 = CodePagesEncodingProvider.Instance.GetEncoding(936)!;
        var gb = gb2312.GetBytes("你好世界");
        Assert.AreEqual(gb2312, EncodingDetector.DetectEncoding(gb));
        byte[] nonEncode = [0xfe, 0x5f, 0xa1];
        Assert.AreEqual(Encoding.Default, EncodingDetector.DetectEncoding(nonEncode));
    }

    [TestMethod]
    [DataRow(1200)]
    [DataRow(1201)]
    [DataRow(12000)]
    [DataRow(12001)]
    public void DetectEncoding_FromBeginningPrefersBomAndRestoresPosition(int codePage)
    {
        var encoding = Encoding.GetEncoding(codePage);
        using var stream = new MemoryStream(encoding.GetPreamble().Concat(encoding.GetBytes("测试正文")).ToArray());
        stream.Position = encoding.GetPreamble().Length;

        Assert.AreEqual(codePage, EncodingDetector.DetectEncoding(stream, readFromBegin: true).CodePage);
        Assert.AreEqual(encoding.GetPreamble().Length, stream.Position);
    }

    [TestMethod]
    public void DetectEncoding_KeepsUtf8WhenSampleSplitsACharacter()
    {
        var bytes = Encoding.UTF8.GetBytes(new string('a', 1023) + "边界");
        Assert.AreEqual(Encoding.UTF8, EncodingDetector.DetectEncoding(bytes));
    }
}
