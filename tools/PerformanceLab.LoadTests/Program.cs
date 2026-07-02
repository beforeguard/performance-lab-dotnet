using NBomber.CSharp;

var httpClientHandler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback =
        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
};

using var httpClient = new HttpClient(httpClientHandler);

var scenario = Scenario.Create(
    "users_baseline",
    async context =>
    {
        var response = await httpClient.GetAsync(
            "https://localhost:5206/users");

        return response.IsSuccessStatusCode
            ? Response.Ok()
            : Response.Fail();
    })
    .WithLoadSimulations(
        Simulation.Inject(
            rate: 50,
            interval: TimeSpan.FromSeconds(1),
            during: TimeSpan.FromMinutes(1)));

NBomberRunner
    .RegisterScenarios(scenario)
    .Run();