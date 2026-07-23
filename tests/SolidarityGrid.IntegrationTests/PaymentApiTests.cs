using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SolidarityGrid.Application.Payments;
using SolidarityGrid.Domain.Payments;
using SolidarityGrid.Node.Diagnostics;
using SolidarityGrid.Node.Payments;
using Xunit;

namespace SolidarityGrid.IntegrationTests;

public sealed class PaymentApiTests : IClassFixture<ValidNodeFactory>
{
    private readonly ValidNodeFactory _factory;

    public PaymentApiTests(ValidNodeFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ValidPostReturnsAcceptedContractAndCanBeQueried()
    {
        using var client = _factory.CreateClient();
        var key = UniqueKey();
        using var request = CreatePost(key, 150000m, "cop");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "api-correlation");

        using var response = await client.SendAsync(request);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("false", Header(response, PaymentEndpoints.IdempotencyReplayedHeaderName));
        Assert.Equal("api-correlation", Header(response, CorrelationIdMiddleware.HeaderName));
        Assert.Equal(key, body.GetProperty("idempotencyKey").GetString());
        Assert.Equal(150000m, body.GetProperty("amount").GetDecimal());
        Assert.Equal("COP", body.GetProperty("currency").GetString());
        Assert.Equal("Replicated", body.GetProperty("status").GetString());
        Assert.Equal(2, body.GetProperty("version").GetInt64());
        Assert.Equal("test-node", body.GetProperty("acceptedByNodeId").GetString());
        Assert.False(body.GetProperty("isReplay").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("ownerNodeId").ValueKind);
        var paymentId = body.GetProperty("paymentId").GetGuid();
        Assert.Equal($"/payments/{paymentId:D}", response.Headers.Location?.ToString());

        using var getResponse = await client.GetAsync($"/payments/{paymentId:D}");
        var getBody = await ReadJsonAsync(getResponse);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(paymentId, getBody.GetProperty("paymentId").GetGuid());
        Assert.False(getBody.TryGetProperty("acceptedByNodeId", out _));
        Assert.False(getBody.TryGetProperty("isReplay", out _));
    }

    [Fact]
    public async Task ExactReplayReturnsSameResourceWithoutChangingVersionOrTimestamp()
    {
        using var client = _factory.CreateClient();
        var key = UniqueKey();
        using var createdResponse = await client.SendAsync(CreatePost(key, 50m, "USD"));
        var created = await ReadJsonAsync(createdResponse);

        using var replayResponse = await client.SendAsync(CreatePost(key, 50m, "usd"));
        var replay = await ReadJsonAsync(replayResponse);

        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.Equal("true", Header(replayResponse, PaymentEndpoints.IdempotencyReplayedHeaderName));
        Assert.True(replay.GetProperty("isReplay").GetBoolean());
        Assert.Equal(
            created.GetProperty("paymentId").GetGuid(),
            replay.GetProperty("paymentId").GetGuid());
        Assert.Equal(
            created.GetProperty("version").GetInt64(),
            replay.GetProperty("version").GetInt64());
        Assert.Equal(
            created.GetProperty("updatedAtUtc").GetDateTimeOffset(),
            replay.GetProperty("updatedAtUtc").GetDateTimeOffset());
        Assert.Equal(1, await CountByKeyAsync(_factory.DatabasePath, key));
    }

    [Theory]
    [InlineData(51, "USD")]
    [InlineData(50, "EUR")]
    public async Task IncompatibleReplayReturnsProblemConflict(
        decimal amount,
        string currency)
    {
        using var client = _factory.CreateClient();
        var key = UniqueKey();
        using var createdResponse = await client.SendAsync(CreatePost(key, 50m, "USD"));
        Assert.Equal(HttpStatusCode.Accepted, createdResponse.StatusCode);

        using var conflictResponse =
            await client.SendAsync(CreatePost(key, amount, currency));
        var problem = await ReadJsonAsync(conflictResponse);

        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
        Assert.Equal(
            "application/problem+json",
            conflictResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "IDEMPOTENCY_KEY_PAYLOAD_CONFLICT",
            problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            problem.GetProperty("correlationId").GetString()));
        Assert.DoesNotContain(
            "Sqlite",
            await conflictResponse.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await CountByKeyAsync(_factory.DatabasePath, key));
    }

    [Fact]
    public async Task CaseSensitiveKeysCreateDifferentPayments()
    {
        using var client = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var upperKey = $"PAY-{suffix}";
        var lowerKey = $"pay-{suffix}";

        using var upperResponse =
            await client.SendAsync(CreatePost(upperKey, 10m, "USD"));
        using var lowerResponse =
            await client.SendAsync(CreatePost(lowerKey, 10m, "USD"));
        var upper = await ReadJsonAsync(upperResponse);
        var lower = await ReadJsonAsync(lowerResponse);

        Assert.Equal(HttpStatusCode.Accepted, upperResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, lowerResponse.StatusCode);
        Assert.NotEqual(
            upper.GetProperty("paymentId").GetGuid(),
            lower.GetProperty("paymentId").GetGuid());
    }

    [Theory]
    [InlineData(null, 10, "USD", PaymentErrorCodes.IdempotencyKeyRequired)]
    [InlineData("", 10, "USD", PaymentErrorCodes.IdempotencyKeyRequired)]
    [InlineData("VALID", 0, "USD", PaymentErrorCodes.PaymentAmountMustBePositive)]
    [InlineData("VALID", -1, "USD", PaymentErrorCodes.PaymentAmountMustBePositive)]
    [InlineData("VALID", 10, "", PaymentErrorCodes.PaymentCurrencyInvalid)]
    [InlineData("VALID", 10, "US", PaymentErrorCodes.PaymentCurrencyInvalid)]
    public async Task InvalidRequestReturnsBadRequestWithStableCode(
        string? key,
        decimal amount,
        string currency,
        string expectedCode)
    {
        using var client = _factory.CreateClient();
        var actualKey = key == "VALID" ? UniqueKey() : key;
        var before = await CountPaymentsAsync(_factory.DatabasePath);
        using var response = await client.SendAsync(
            CreatePost(actualKey, amount, currency));
        var problem = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedCode, problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            problem.GetProperty("correlationId").GetString()));
        Assert.Equal(before, await CountPaymentsAsync(_factory.DatabasePath));
    }

    [Fact]
    public async Task TooLongHeaderReturnsBadRequest()
    {
        using var client = _factory.CreateClient();
        var key = new string('X', IdempotencyKey.MaximumLength + 1);

        using var response = await client.SendAsync(CreatePost(key, 10m, "USD"));
        var problem = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            PaymentErrorCodes.IdempotencyKeyTooLong,
            problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task InvalidJsonReturnsSafeBadRequestProblem()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/pay")
        {
            Content = new StringContent("{", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(PaymentEndpoints.IdempotencyKeyHeaderName, UniqueKey());

        using var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            PaymentApplicationErrorCodes.PaymentInvalidRequest,
            problem.RootElement.GetProperty("code").GetString());
        Assert.True(problem.RootElement.TryGetProperty("correlationId", out _));
        Assert.DoesNotContain("stack", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingPaymentReturnsNotFoundProblem()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync($"/payments/{Guid.NewGuid():D}");
        var problem = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            PaymentApplicationErrorCodes.PaymentNotFound,
            problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ConcurrentIdenticalRequestsCreateExactlyOnePayment()
    {
        using var client = _factory.CreateClient();
        var key = UniqueKey();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 12)
                .Select(_ => client.SendAsync(CreatePost(key, 99m, "USD"))));
        try
        {
            var bodies = await Task.WhenAll(responses.Select(ReadJsonAsync));
            Assert.All(
                responses,
                response => Assert.Contains(
                    response.StatusCode,
                    new[] { HttpStatusCode.Accepted, HttpStatusCode.OK }));
            Assert.True(
                responses.Count(response => response.StatusCode == HttpStatusCode.Accepted) <= 1);
            Assert.Single(
                bodies.Select(body => body.GetProperty("paymentId").GetGuid()).Distinct());
            Assert.All(
                responses,
                response => Assert.NotEqual(
                    HttpStatusCode.InternalServerError,
                    response.StatusCode));
            Assert.Equal(1, await CountByKeyAsync(_factory.DatabasePath, key));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task ConcurrentIncompatibleRequestsProduceOneWinningPayload()
    {
        using var client = _factory.CreateClient();
        var key = UniqueKey();
        var requests = Enumerable.Range(0, 12)
            .Select(index => index % 2 == 0 ? 100m : 200m)
            .ToArray();

        var responses = await Task.WhenAll(
            requests.Select(amount =>
                client.SendAsync(CreatePost(key, amount, "USD"))));
        try
        {
            var storedAmount = await ReadAmountByKeyAsync(_factory.DatabasePath, key);
            Assert.Equal(1, await CountByKeyAsync(_factory.DatabasePath, key));
            Assert.True(
                responses.Count(response => response.StatusCode == HttpStatusCode.Accepted) <= 1);
            Assert.DoesNotContain(
                responses,
                response => response.StatusCode == HttpStatusCode.InternalServerError);

            for (var index = 0; index < responses.Length; index++)
            {
                HttpStatusCode[] expected = requests[index] == storedAmount
                    ? [HttpStatusCode.Accepted, HttpStatusCode.OK]
                    : [HttpStatusCode.Conflict];
                Assert.Contains(responses[index].StatusCode, expected);
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task PaymentSurvivesHostRestartAndReplayKeepsId()
    {
        var directory = ValidNodeFactory.CreateDatabaseDirectory();
        try
        {
            var key = UniqueKey();
            Guid paymentId;
            using (var firstFactory = new ValidNodeFactory(
                       directory,
                       "restart-node",
                       deleteDatabaseOnDispose: false))
            using (var firstClient = firstFactory.CreateClient())
            using (var createdResponse =
                   await firstClient.SendAsync(CreatePost(key, 70m, "EUR")))
            {
                paymentId = (await ReadJsonAsync(createdResponse))
                    .GetProperty("paymentId")
                    .GetGuid();
            }

            using var secondFactory = new ValidNodeFactory(
                directory,
                "restart-node",
                deleteDatabaseOnDispose: false);
            using var secondClient = secondFactory.CreateClient();
            using var getResponse =
                await secondClient.GetAsync($"/payments/{paymentId:D}");
            using var replayResponse =
                await secondClient.SendAsync(CreatePost(key, 70m, "EUR"));
            var replay = await ReadJsonAsync(replayResponse);

            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
            Assert.Equal(paymentId, replay.GetProperty("paymentId").GetGuid());
            Assert.Equal(1, await CountByKeyAsync(
                Path.Combine(directory, "node.db"),
                key));
        }
        finally
        {
            ValidNodeFactory.DeleteDatabaseDirectory(directory);
        }
    }

    [Fact]
    public async Task IndependentNodeDoesNotShareCentralDatabase()
    {
        using var nodeAFactory = new ValidNodeFactory(
            ValidNodeFactory.CreateDatabaseDirectory(),
            "node-a-test",
            deleteDatabaseOnDispose: true);
        using var nodeBFactory = new ValidNodeFactory(
            ValidNodeFactory.CreateDatabaseDirectory(),
            "node-b-test",
            deleteDatabaseOnDispose: true);
        using var nodeA = nodeAFactory.CreateClient();
        using var nodeB = nodeBFactory.CreateClient();
        var key = UniqueKey();

        using var createdResponse =
            await nodeA.SendAsync(CreatePost(key, 80m, "COP"));
        var created = await ReadJsonAsync(createdResponse);
        var paymentId = created.GetProperty("paymentId").GetGuid();
        using var missingResponse =
            await nodeB.GetAsync($"/payments/{paymentId:D}");

        Assert.Equal(HttpStatusCode.Accepted, createdResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.NotEqual(nodeAFactory.DatabasePath, nodeBFactory.DatabasePath);
    }

    private static HttpRequestMessage CreatePost(
        string? key,
        decimal amount,
        string currency)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/pay")
        {
            Content = JsonContent.Create(new { amount, currency }),
        };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation(
                PaymentEndpoints.IdempotencyKeyHeaderName,
                key);
        }

        return request;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var document = await response.Content.ReadFromJsonAsync<JsonDocument>();
        return Assert.IsType<JsonDocument>(document).RootElement.Clone();
    }

    private static string Header(HttpResponseMessage response, string name) =>
        Assert.Single(response.Headers.GetValues(name));

    private static string UniqueKey() => $"HTTP-{Guid.NewGuid():N}";

    private static async Task<long> CountPaymentsAsync(string databasePath) =>
        Convert.ToInt64(
            await ExecuteScalarAsync(databasePath, "SELECT COUNT(*) FROM payments;"),
            System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<long> CountByKeyAsync(
        string databasePath,
        string key) =>
        Convert.ToInt64(
            await ExecuteScalarAsync(
                databasePath,
                "SELECT COUNT(*) FROM payments WHERE idempotency_key = $key;",
                key),
            System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<decimal> ReadAmountByKeyAsync(
        string databasePath,
        string key) =>
        decimal.Parse(
            Convert.ToString(
                await ExecuteScalarAsync(
                    databasePath,
                    "SELECT amount FROM payments WHERE idempotency_key = $key;",
                    key),
                System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<object?> ExecuteScalarAsync(
        string databasePath,
        string commandText,
        string? key = null)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        if (key is not null)
        {
            command.Parameters.AddWithValue("$key", key);
        }

        return await command.ExecuteScalarAsync();
    }
}
