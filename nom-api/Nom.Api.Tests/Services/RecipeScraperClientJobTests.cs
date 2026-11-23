using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nom.Orch.Settings;
using Nom.Orch.UtilityInterfaces;
using Nom.Orch.UtilityServices;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The scraper client's job routes: every way a call can go wrong comes back as an answer the
    /// source lane counts (busy, lost, failed), never as an empty job or an exception.
    /// </summary>
    public class RecipeScraperClientJobTests
    {
        private sealed class Handler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _answer;

            public Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) => _answer = answer;

            public List<(HttpMethod Method, string Path, string? Body)> Calls { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls.Add((request.Method, request.RequestUri!.AbsolutePath, request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
                return _answer(request);
            }
        }

        private static (RecipeScraperClient Client, Handler Handler) Client(Func<HttpRequestMessage, HttpResponseMessage> answer)
        {
            var handler = new Handler(answer);
            var settings = Options.Create(new RecipeScraperSettings { BaseUrl = "http://scraper.test", ApiKey = "test-key" });
            return (new RecipeScraperClient(new HttpClient(handler), settings, NullLogger<RecipeScraperClient>.Instance), handler);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        [Fact]
        public async Task An_accepted_job_is_read_with_its_result()
        {
            var (client, handler) = Client(_ => Json(HttpStatusCode.Accepted,
                "{\"jobId\":\"abc\",\"kind\":\"discovery\",\"status\":\"running\",\"budgetSeconds\":600,\"deadlineUtc\":\"2026-11-20T19:10:00+00:00\",\"result\":{\"candidates\":[],\"partial\":false}}"));

            var answer = await client.StartDiscoveryJobAsync(new[] { "a.example", "b.example", "c.example" }, 10, 600, new[] { "forbidden.example" });

            answer.Outcome.Should().Be(ScraperJobOutcome.Ok);
            answer.Job!.JobId.Should().Be("abc");
            answer.Job.DeadlineUtc.Should().Be(new DateTimeOffset(2026, 11, 20, 19, 10, 0, TimeSpan.Zero));
            answer.Job.Result.Should().NotBeNull();
            handler.Calls.Should().ContainSingle(c => c.Method == HttpMethod.Post && c.Path == "/api/discover/jobs" && c.Body!.Contains("\"budgetSeconds\":600") && c.Body.Contains("\"excludeDomains\":[\"forbidden.example\"]"));
        }

        [Fact]
        public async Task A_source_check_job_asks_for_the_sampled_recipes_and_is_read_by_its_own_route()
        {
            var (client, handler) = Client(_ => Json(HttpStatusCode.OK, "{\"jobId\":\"x1\",\"kind\":\"source-check\",\"status\":\"completed\"}"));

            await client.StartSourceCheckJobAsync("www.open.example", 20, 1200);
            await client.GetJobAsync("source-check", "x1");
            await client.GetJobAsync("discovery", "d1");

            handler.Calls[0].Path.Should().Be("/api/source-check/jobs");
            handler.Calls[0].Body.Should().Contain("\"sampleSize\":20").And.Contain("\"includeRecipes\":true");
            handler.Calls[1].Path.Should().Be("/api/source-check/jobs/x1");
            handler.Calls[2].Path.Should().Be("/api/discover/jobs/d1");
        }

        [Theory]
        [InlineData(HttpStatusCode.TooManyRequests, ScraperJobOutcome.Busy)]
        [InlineData(HttpStatusCode.NotFound, ScraperJobOutcome.NotFound)]
        [InlineData(HttpStatusCode.InternalServerError, ScraperJobOutcome.Failed)]
        [InlineData(HttpStatusCode.Unauthorized, ScraperJobOutcome.Failed)]
        public async Task An_error_status_is_an_answer_not_an_empty_job(HttpStatusCode status, ScraperJobOutcome expected)
        {
            var (client, _) = Client(_ => Json(status, "{\"error\":\"x\"}"));

            var answer = await client.GetJobAsync("discovery", "abc");

            answer.Outcome.Should().Be(expected);
            answer.Job.Should().BeNull();
            answer.Error.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task A_transport_error_or_an_unreadable_answer_is_a_failure()
        {
            var (down, _) = Client(_ => throw new HttpRequestException("connection refused"));
            (await down.GetJobAsync("discovery", "abc")).Outcome.Should().Be(ScraperJobOutcome.Failed);

            var (garbled, _) = Client(_ => Json(HttpStatusCode.OK, "not json"));
            (await garbled.GetJobAsync("discovery", "abc")).Outcome.Should().Be(ScraperJobOutcome.Failed);

            var (empty, _) = Client(_ => Json(HttpStatusCode.OK, "{}"));
            (await empty.GetJobAsync("discovery", "abc")).Outcome.Should().Be(ScraperJobOutcome.Failed);
        }

        [Fact]
        public async Task Without_a_scraper_nothing_is_called()
        {
            var client = new RecipeScraperClient(new HttpClient(new Handler(_ => throw new InvalidOperationException())),
                Options.Create(new RecipeScraperSettings()), NullLogger<RecipeScraperClient>.Instance);

            (await client.StartSourceCheckJobAsync("a.example", 20, 1200)).Outcome.Should().Be(ScraperJobOutcome.Failed);
        }
    }
}
