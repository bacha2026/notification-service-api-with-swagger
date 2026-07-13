using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace NSA.Tests;

public sealed class ApiContractTests : IClassFixture<NsaApiFactory>
{
    private readonly HttpClient client;

    public ApiContractTests(NsaApiFactory factory)
    {
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Swagger_documents_are_available_and_version_specific()
    {
        using var v1Response = await client.GetAsync("/swagger/v1/swagger.json");
        using var v2Response = await client.GetAsync("/swagger/v2/swagger.json");

        Assert.Equal(HttpStatusCode.OK, v1Response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, v2Response.StatusCode);

        using var v1 = JsonDocument.Parse(await v1Response.Content.ReadAsStringAsync());
        using var v2 = JsonDocument.Parse(await v2Response.Content.ReadAsStringAsync());
        var v1Paths = v1.RootElement.GetProperty("paths").EnumerateObject().Select(path => path.Name).ToArray();
        var v2Paths = v2.RootElement.GetProperty("paths").EnumerateObject().Select(path => path.Name).ToArray();

        Assert.NotEmpty(v1Paths);
        Assert.NotEmpty(v2Paths);
        Assert.All(v1Paths, path => Assert.DoesNotContain("{version}", path, StringComparison.Ordinal));
        Assert.All(v2Paths, path => Assert.DoesNotContain("{version}", path, StringComparison.Ordinal));
        Assert.Contains(v1Paths, path => path.StartsWith("/api/v1/", StringComparison.Ordinal));
        Assert.Contains(v2Paths, path => path.StartsWith("/api/v2/", StringComparison.Ordinal));
        Assert.DoesNotContain(v1Paths, path => path.StartsWith("/api/v2/", StringComparison.Ordinal));
        Assert.DoesNotContain(v2Paths, path => path.StartsWith("/api/v1/", StringComparison.Ordinal));

        var getById = v2.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v2/notifications/{id}")
            .GetProperty("get");
        Assert.True(getById.GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(getById.GetProperty("responses").TryGetProperty("404", out _));
        Assert.False(getById.GetProperty("responses").TryGetProperty("400", out _));

        var notificationOperations = v2.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v2/notifications");
        var queryValidationResponse = notificationOperations
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("400");
        Assert.EndsWith(
            "/ValidationProblemDetails",
            GetProblemDetailsSchemaReference(queryValidationResponse),
            StringComparison.Ordinal);

        var bodyValidationResponse = notificationOperations
            .GetProperty("post")
            .GetProperty("responses")
            .GetProperty("400");
        Assert.EndsWith(
            "/ValidationProblemDetails",
            GetProblemDetailsSchemaReference(bodyValidationResponse),
            StringComparison.Ordinal);

        var schemas = v2.RootElement.GetProperty("components").GetProperty("schemas");
        var createNotification = schemas.GetProperty("CreateNotificationRequest");
        AssertRequiredProperties(createNotification, "recipientEmail", "subject", "body");
        var notificationProperties = createNotification.GetProperty("properties");
        Assert.Equal("email", notificationProperties.GetProperty("recipientEmail").GetProperty("format").GetString());
        Assert.Equal(320, notificationProperties.GetProperty("recipientEmail").GetProperty("maxLength").GetInt32());
        Assert.Equal(200, notificationProperties.GetProperty("subject").GetProperty("maxLength").GetInt32());
        Assert.Equal(4_000, notificationProperties.GetProperty("body").GetProperty("maxLength").GetInt32());

        var bulk = schemas.GetProperty("CreateBulkNotificationsRequest");
        AssertRequiredProperties(bulk, "notifications");
        var notifications = bulk.GetProperty("properties").GetProperty("notifications");
        Assert.Equal(1, notifications.GetProperty("minItems").GetInt32());
        Assert.Equal(100, notifications.GetProperty("maxItems").GetInt32());

        var createProduct = schemas.GetProperty("CreateProductRequest");
        AssertRequiredProperties(createProduct, "name", "shortDescription", "description", "imageUrl");
        var productProperties = createProduct.GetProperty("properties");
        Assert.Equal(160, productProperties.GetProperty("name").GetProperty("maxLength").GetInt32());
        Assert.Equal(0m, productProperties.GetProperty("price").GetProperty("minimum").GetDecimal());
        Assert.Equal(0, productProperties.GetProperty("quantityAvailable").GetProperty("minimum").GetInt32());
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v1.0")]
    public async Task Version_one_is_callable_and_advertises_its_deprecation_and_sunset(string version)
    {
        using var response = await client.GetAsync($"/api/{version}/notifications/2147483647");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Deprecation", out var deprecation));
        Assert.Equal("true", Assert.Single(deprecation));
        Assert.True(response.Headers.TryGetValues("Sunset", out var sunset));
        Assert.True(DateTimeOffset.TryParse(Assert.Single(sunset), out _));
    }

    [Fact]
    public async Task Version_two_is_callable_without_deprecation_headers()
    {
        using var response = await client.GetAsync("/api/v2/notifications/2147483647");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains("Deprecation"));
        Assert.False(response.Headers.Contains("Sunset"));
    }

    [Theory]
    [InlineData("GET", "/api/v2/notifications/2147483647", null, 404)]
    [InlineData("GET", "/api/v2/not-a-route", null, 404)]
    [InlineData("PATCH", "/api/v2/notifications", null, 405)]
    [InlineData("GET", "/api/v3/notifications", null, 404)]
    [InlineData("POST", "/api/v2/notifications", "{", 400)]
    [InlineData("POST", "/api/v2/notifications", "", 400)]
    public async Task Common_HTTP_errors_use_the_same_problem_details_contract(
        string method,
        string uri,
        string? json,
        int expectedStatus)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), uri);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request);

        await AssertProblemDetailsAsync(response, expectedStatus, uri.Split('?')[0]);
    }

    [Fact]
    public async Task Unsupported_media_type_uses_problem_details()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/notifications")
        {
            Content = new StringContent("not json", Encoding.UTF8, "text/plain")
        };

        using var response = await client.SendAsync(request);

        await AssertProblemDetailsAsync(response, 415, "/api/v2/notifications");
    }

    [Fact]
    public async Task Domain_validation_error_uses_problem_details_and_retains_v1_headers()
    {
        using var response = await client.PostAsJsonAsync("/api/v1/notifications", new
        {
            recipientEmail = " ",
            channel = 1,
            subject = "Subject",
            body = "Body",
            orderId = (int?)null
        });

        await AssertProblemDetailsAsync(response, 400, "/api/v1/notifications");
        Assert.True(response.Headers.TryGetValues("Deprecation", out var values));
        Assert.Equal("true", Assert.Single(values));
        Assert.True(response.Headers.Contains("Sunset"));
    }

    [Theory]
    [MemberData(nameof(InvalidNotificationPayloads))]
    public async Task Invalid_notification_contracts_are_rejected_before_persistence(string json)
    {
        using var response = await client.PostAsync(
            "/api/v2/notifications",
            new StringContent(json, Encoding.UTF8, "application/json"));

        await AssertProblemDetailsAsync(response, 400, "/api/v2/notifications");
    }

    [Fact]
    public async Task Created_notification_location_preserves_the_requested_API_version_and_is_gettable()
    {
        using var response = await client.PostAsJsonAsync("/api/v2/notifications", new
        {
            recipientEmail = "created@example.com",
            channel = 1,
            subject = "Created",
            body = "Body",
            orderId = (int?)null
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.StartsWith(
            "/api/v2/notifications/",
            GetLocationPath(response.Headers.Location!),
            StringComparison.OrdinalIgnoreCase);

        using var getResponse = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task Notification_CRUD_smoke_flow_succeeds_end_to_end()
    {
        var recipient = $"crud-{Guid.NewGuid():N}@example.com";
        using var createResponse = await client.PostAsJsonAsync("/api/v2/notifications", new
        {
            recipientEmail = recipient,
            channel = 1,
            subject = "Created subject",
            body = "Created body",
            orderId = (int?)null
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetInt32();

        using var listResponse = await client.GetAsync(
            $"/api/v2/notifications?recipientEmail={Uri.EscapeDataString(recipient)}");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var listed = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        Assert.Contains(listed.RootElement.EnumerateArray(), item => item.GetProperty("id").GetInt32() == id);

        using var updateResponse = await client.PutAsJsonAsync($"/api/v2/notifications/{id}", new
        {
            recipientEmail = recipient,
            channel = 1,
            subject = "Updated subject",
            body = "Updated body",
            orderId = (int?)null,
            isRead = true
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        using var updated = JsonDocument.Parse(await updateResponse.Content.ReadAsStringAsync());
        Assert.Equal("Updated subject", updated.RootElement.GetProperty("subject").GetString());
        Assert.True(updated.RootElement.GetProperty("isRead").GetBoolean());

        using var deleteResponse = await client.DeleteAsync($"/api/v2/notifications/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        using var deletedGetResponse = await client.GetAsync($"/api/v2/notifications/{id}");
        await AssertProblemDetailsAsync(deletedGetResponse, 404, $"/api/v2/notifications/{id}");
    }

    [Fact]
    public async Task Product_catalog_smoke_and_validation_paths_are_available()
    {
        using var listResponse = await client.GetAsync("/api/v2/products");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var products = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        Assert.True(products.RootElement.GetArrayLength() >= 5);

        using var detailResponse = await client.GetAsync("/api/v2/products/1");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using var product = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, product.RootElement.GetProperty("id").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(product.RootElement.GetProperty("name").GetString()));

        using var invalidResponse = await client.PostAsJsonAsync("/api/v2/products", new
        {
            name = "Invalid",
            shortDescription = "Invalid product",
            description = "Invalid because its price is negative.",
            price = -0.01m,
            quantityAvailable = 1,
            imageUrl = "https://example.com/image.png"
        });
        await AssertProblemDetailsAsync(invalidResponse, 400, "/api/v2/products");
    }

    [Fact]
    public async Task Seeded_cart_and_order_tracking_routes_pass_smoke_checks()
    {
        const string visitor = "bmacha2015@gmail.com";
        using var cartResponse = await client.GetAsync(
            $"/api/v2/cart/{Uri.EscapeDataString(visitor)}");
        Assert.Equal(HttpStatusCode.OK, cartResponse.StatusCode);
        using var cart = JsonDocument.Parse(await cartResponse.Content.ReadAsStringAsync());
        Assert.Equal(visitor, cart.RootElement.GetProperty("visitorEmail").GetString());
        Assert.NotEmpty(cart.RootElement.GetProperty("items").EnumerateArray());

        using var ordersResponse = await client.GetAsync(
            $"/api/v2/orders?visitorEmail={Uri.EscapeDataString(visitor)}");
        Assert.Equal(HttpStatusCode.OK, ordersResponse.StatusCode);
        using var orders = JsonDocument.Parse(await ordersResponse.Content.ReadAsStringAsync());
        Assert.NotEmpty(orders.RootElement.EnumerateArray());

        using var detailResponse = await client.GetAsync("/api/v2/orders/1");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using var order = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, order.RootElement.GetProperty("id").GetInt32());
        Assert.NotEmpty(order.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Bulk_submission_returns_202_with_a_stable_job_location_and_reaches_a_terminal_state()
    {
        using var response = await client.PostAsJsonAsync("/api/v2/notifications/bulk", new
        {
            notifications = new[]
            {
                new
                {
                    recipientEmail = "first@example.com",
                    channel = 1,
                    subject = "First",
                    body = "Body",
                    orderId = (int?)null
                },
                new
                {
                    recipientEmail = "second@example.com",
                    channel = 1,
                    subject = "Second",
                    body = "Body",
                    orderId = (int?)null
                }
            }
        });

        Assert.True(
            response.StatusCode == HttpStatusCode.Accepted,
            $"Expected 202 Accepted but received {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.NotNull(response.Headers.Location);
        using var accepted = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var jobId = accepted.RootElement.GetProperty("jobId").GetGuid();
        Assert.NotEqual(Guid.Empty, jobId);
        Assert.StartsWith(
            "/api/v2/notifications/bulk/",
            GetLocationPath(response.Headers.Location!),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(jobId.ToString(), response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            using var statusResponse = await client.GetAsync(response.Headers.Location, timeout.Token);
            Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
            using var status = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync(timeout.Token));
            var state = status.RootElement.GetProperty("status").GetString();
            if (state is "Completed" or "CompletedWithErrors")
            {
                Assert.Equal(2, status.RootElement.GetProperty("processedCount").GetInt32());
                break;
            }

            await Task.Delay(10, timeout.Token);
        }
    }

    public static IEnumerable<object[]> InvalidNotificationPayloads()
    {
        yield return new object[]
        {
            """{"recipientEmail":"not-an-email","channel":1,"subject":"Subject","body":"Body","orderId":null}"""
        };
        yield return new object[]
        {
            """{"recipientEmail":"person@example.com","channel":999,"subject":"Subject","body":"Body","orderId":null}"""
        };
        yield return new object[]
        {
            """{"recipientEmail":"person@example.com","channel":1,"subject":"Subject","body":"Body","orderId":0}"""
        };
        yield return new object[]
        {
            JsonSerializer.Serialize(new
            {
                recipientEmail = "person@example.com",
                channel = 1,
                subject = new string('s', 201),
                body = "Body",
                orderId = (int?)null
            })
        };
    }

    private static string GetLocationPath(Uri location) =>
        location.IsAbsoluteUri
            ? location.AbsolutePath
            : location.OriginalString.Split('?', '#')[0];

    private static string GetProblemDetailsSchemaReference(JsonElement response) =>
        response
            .GetProperty("content")
            .GetProperty("application/problem+json")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString()!;

    private static void AssertRequiredProperties(JsonElement schema, params string[] expectedProperties)
    {
        var required = schema
            .GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(expectedProperties, property => Assert.Contains(property, required));
    }

    private static async Task AssertProblemDetailsAsync(
        HttpResponseMessage response,
        int expectedStatus,
        string expectedInstance)
    {
        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = problem.RootElement;
        Assert.Equal(expectedStatus, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));
        Assert.Equal(expectedInstance, root.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
    }
}
