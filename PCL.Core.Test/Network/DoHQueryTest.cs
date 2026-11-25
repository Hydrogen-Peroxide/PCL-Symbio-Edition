using System;
using System.Buffers.Binary;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Ae.Dns.Client;
using Ae.Dns.Protocol;
using Ae.Dns.Protocol.Enums;
using Ae.Dns.Protocol.Records;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.IO.Net.Dns;

namespace PCL.Core.Test.Network;

[TestClass]
public class DoHQueryTest
{
    [TestMethod]
    public async Task TestIpQuery()
    {
        var query = DnsQuery.Instance;
        var addr = await query.QueryForIpAsync("cloudflare.com", TestContext.CancellationTokenSource.Token);
        Assert.IsNotNull(addr);
        Assert.IsGreaterThan(0, addr.Length);
        Console.WriteLine(string.Join(", ", addr.Select(x => x.ToString())));
    }

    [TestMethod]
    public async Task TestSrvQuery()
    {
        var query = DnsQueryFactory.CreateQuery("_minecraft._tcp.example.test", DnsQueryType.SRV);
        var response = Convert.FromHexString(
            "000081800001000100000000" + // Header with one question and one answer
            "0A5F6D696E656372616674045F746370076578616D706C6504746573740000210001" + // SRV question
            "C00C002100010000003C0019" + // Answer header referring to the question's name
            "000A000563DD0467616D65076578616D706C65047465737400"); // Priority, weight, port, target
        BinaryPrimitives.WriteUInt16BigEndian(response, query.Header.Id);

        using var http = new HttpClient(new WireResponseHandler(response))
        {
            BaseAddress = new Uri("https://dns.example.test/")
        };
        using var client = new DnsHttpClient(http);
        var addr = await client.Query(query, TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(DnsResponseCode.NoError, addr.Header.ResponseCode);
        Assert.AreEqual(1, addr.Answers.Count);
        Assert.AreEqual(DnsQueryClass.IN, addr.Header.QueryClass);
        var record = addr.Answers.FirstOrDefault()?.Resource as DnsUnknownResource;
        Assert.IsNotNull(record);
        var srvRecord = new DnsSrvResource();
        var offset = 0;
        srvRecord.ReadBytes(record.Raw, ref offset, record.Raw.Length);
        Assert.AreEqual("game.example.test", srvRecord.Target);
        Assert.AreEqual(25565, srvRecord.Port);
        Assert.AreEqual(10, srvRecord.Priority);
        Assert.AreEqual(5, srvRecord.Weight);
    }

    private sealed class WireResponseHandler(byte[] response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.AreEqual("/dns-query", request.RequestUri?.AbsolutePath);
            var content = new ByteArrayContent(response);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    public TestContext TestContext { get; set; }
}
