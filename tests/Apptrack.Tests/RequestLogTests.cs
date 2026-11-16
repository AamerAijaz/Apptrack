using System.Diagnostics;
using Apptrack;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Apptrack.Tests;

public sealed class RequestLogTests
{
    [Fact]
    public async Task Completed_request_records_path_status_and_duration()
    {
        var context = Context(HttpMethods.Get, "/api/notices");
        context.Request.QueryString = new QueryString("?code=secret");
        var logger = new CollectingLogger();

        const int delayMs = 40;
        await RequestLog.InvokeAsync(context, ctx =>
        {
            var wait = Stopwatch.StartNew();
            while (wait.ElapsedMilliseconds < delayMs)
            {
                Thread.SpinWait(1);
            }

            ctx.Response.StatusCode = StatusCodes.Status201Created;
            return Task.CompletedTask;
        }, logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        AssertLine(entry.Line, "GET", "/api/notices", StatusCodes.Status201Created, delayMs);
        Assert.DoesNotContain("secret", entry.Line);
    }

    [Fact]
    public async Task Unhandled_exception_before_the_response_records_500()
    {
        var context = Context(HttpMethods.Post, "/api/notices");
        var logger = new CollectingLogger();

        var thrown = await Record.ExceptionAsync(() =>
            RequestLog.InvokeAsync(context, _ => throw new InvalidOperationException("boom"), logger));

        Assert.IsType<InvalidOperationException>(thrown);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        AssertLine(entry.Line, "POST", "/api/notices", StatusCodes.Status500InternalServerError, 0);
        Assert.DoesNotContain("boom", entry.Line);
    }

    [Fact]
    public async Task Started_response_keeps_its_status_when_the_pipeline_throws()
    {
        var context = Context(HttpMethods.Patch, "/api/notices/8d8b6c1e-1c2a-4b3d-9e0f-112233445566");
        context.Features.Set<IHttpResponseFeature>(new StartedResponse(StatusCodes.Status409Conflict));
        var logger = new CollectingLogger();

        var thrown = await Record.ExceptionAsync(() =>
            RequestLog.InvokeAsync(context, _ => throw new InvalidOperationException("after start"), logger));

        Assert.IsType<InvalidOperationException>(thrown);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        AssertLine(entry.Line, "PATCH", "/api/notices/8d8b6c1e-1c2a-4b3d-9e0f-112233445566", StatusCodes.Status409Conflict, 0);
    }

    private static DefaultHttpContext Context(string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        return context;
    }

    private static void AssertLine(string line, string method, string path, int status, long minElapsedMs)
    {
        var parts = line.Split(' ');
        Assert.Equal(4, parts.Length);
        Assert.Equal(method, parts[0]);
        Assert.Equal(path, parts[1]);
        Assert.Equal(status.ToString(), parts[2]);
        Assert.EndsWith("ms", parts[3]);
        var elapsed = long.Parse(parts[3][..^2]);
        Assert.True(elapsed >= minElapsedMs, $"elapsed {elapsed}ms was under {minElapsedMs}ms");
    }

    private sealed class StartedResponse(int statusCode) : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = statusCode;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<(LogLevel Level, string Line)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
