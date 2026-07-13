using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NSA.Tests;

public sealed class DisabledEmailOrderWorkflowTests : IClassFixture<NsaApiFactory>
{
    private readonly HttpClient client;

    public DisabledEmailOrderWorkflowTests(NsaApiFactory factory)
    {
        client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Order_creation_succeeds_and_leaves_email_intents_pending_when_provider_is_disabled()
    {
        const string visitorEmail = "bmacha2015@gmail.com";
        using var orderResponse = await client.PostAsJsonAsync("/api/v2/orders", new
        {
            visitorEmail
        });

        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
        using var order = JsonDocument.Parse(await orderResponse.Content.ReadAsStringAsync());
        var orderId = order.RootElement.GetProperty("id").GetInt32();

        using var notificationsResponse = await client.GetAsync($"/api/v2/notifications?orderId={orderId}");
        Assert.Equal(HttpStatusCode.OK, notificationsResponse.StatusCode);
        using var notifications = JsonDocument.Parse(await notificationsResponse.Content.ReadAsStringAsync());
        var intents = notifications.RootElement.EnumerateArray().ToArray();

        Assert.Equal(2, intents.Length);
        Assert.All(intents, intent =>
        {
            Assert.Equal(orderId, intent.GetProperty("orderId").GetInt32());
            Assert.Equal(JsonValueKind.Null, intent.GetProperty("sentAtUtc").ValueKind);
        });
    }
}
