using System.Net;
using System.Text;
using OpenCodeManager.Application;
using OpenCodeManager.Infrastructure;
namespace OpenCodeManager.IntegrationTests;

public sealed class ApiTests
{
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> action) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Calls++;return Task.FromResult(action(request));}
    }
    [Theory]
    [InlineData(401,"Authentication")]
    [InlineData(403,"Forbidden")]
    [InlineData(404,"NotFound")]
    public async Task SafeReadsClassifyHttpErrors(int status,string code)
    {
        var handler=new Handler(_=>new((HttpStatusCode)status));var api=new OpenCodeApiClient(new HttpClient(handler),new());
        var e=await Assert.ThrowsAsync<OpenCodeApiException>(()=>api.GetInfoAsync(default));Assert.Equal(code,e.Code);Assert.Equal(1,handler.Calls);
    }
    [Theory]
    [InlineData(409,"Conflict")]
    [InlineData(429,"RateLimit")]
    [InlineData(503,"Server")]
    public async Task MutationsAreNeverBlindlyRetried(int status,string code)
    {
        var handler=new Handler(_=>new((HttpStatusCode)status));var api=new OpenCodeApiClient(new HttpClient(handler),new());
        var e=await Assert.ThrowsAsync<OpenCodeApiException>(()=>api.SendPromptAsync("ses_fixture",new("msg_fixture","work",new()),default));Assert.Equal(code,e.Code);Assert.Equal(1,handler.Calls);
    }
    [Fact]
    public async Task DirectoryFilterUsesV2DirectoryQuery()
    {
        var handler=new Handler(request=>{Assert.Equal("/api/session",request.RequestUri!.AbsolutePath);Assert.Contains("directory=%2Ftmp%2Fa%20b",request.RequestUri.Query);return new(HttpStatusCode.OK){Content=new StringContent("{\"data\":[],\"cursor\":{\"next\":null,\"previous\":null}}",Encoding.UTF8,"application/json")};});
        var api=new OpenCodeApiClient(new HttpClient(handler),new());Assert.Empty((await api.GetDirectorySessionsAsync("/tmp/a b",null,default)).Data);
    }
    [Fact]
    public async Task SseReadsNativeDataFramesAndIgnoresHeartbeatAndMalformedJson()
    {
        using var text=new StringReader(": heartbeat\n\ndata: bad\n\ndata: {\"id\":\"ev1\",\"type\":\"session.tool.called\",\ndata: \"data\":{\"sessionID\":\"ses_one\"}}\n\n");
        var frames=new List<OpenCodeEvent>();await foreach(var item in SseParser.ReadAsync(text,default))frames.Add(item);
        Assert.Single(frames);Assert.Equal("ses_one",frames[0].Data.GetProperty("sessionID").GetString());
    }
    [Fact]
    public async Task SseHonorsCancellation()
    {
        using var ct=new CancellationTokenSource();ct.Cancel();using var reader=new StringReader("data: {}\n\n");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>{await foreach(var item in SseParser.ReadAsync(reader,ct.Token)){ }});
    }
}
