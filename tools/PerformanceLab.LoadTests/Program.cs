using NBomber.CSharp;

var httpClientHandler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback =
        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
};

using var httpClient = new HttpClient(httpClientHandler);

var scenario = Scenario.Create(
    "users_50rps_baseline",
    async context =>
    {
        var response = await httpClient.GetAsync("http://localhost:5206/users");

        response.EnsureSuccessStatusCode();

        await response.Content.ReadAsByteArrayAsync();

        return Response.Ok();
    })
    .WithLoadSimulations(
        Simulation.Inject(
            rate: 50,
            interval: TimeSpan.FromSeconds(1),
            during: TimeSpan.FromMinutes(1)));

NBomberRunner
    .RegisterScenarios(scenario)
    .Run();