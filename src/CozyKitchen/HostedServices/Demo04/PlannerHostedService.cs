using System.Text.Json;
using Azure.Identity;
using CozyKitchen.Extensions;
using CozyKitchen.Plugins.Native;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Beta;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Planning;
using Microsoft.SemanticKernel.Planning.Handlebars;

namespace CozyKitchen.HostedServices;
public class PlannerHostedService : IHostedService
{
    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;
    private readonly Kernel _kernel;
    private readonly IHttpClientFactory _httpClientFactory;

    public PlannerHostedService(
        ILogger<NestedFunctionHostedService> logger,
        IConfiguration configuration,
        Kernel kernel,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _kernel = kernel;
        _httpClientFactory = httpClientFactory;

        _kernel.ImportPluginFromPromptDirectory(
            $"{PathExtensions.GetPluginsRootFolder()}/ResumeAssistantPlugin");
        _kernel.ImportPluginFromPromptDirectory(
            $"{PathExtensions.GetPluginsRootFolder()}/TravelAgentPlugin");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var graphClient = GetGraphServiceClient();

        // adding native Plugins
        var graphSkillsPlugin = new GraphUserProfileSkillsPlugin(graphClient);
        _kernel.Plugins.AddFromObject(graphSkillsPlugin, "GraphSkillsPlugin");
        _kernel.ImportPluginFromObject(new MyIpAddressPlugin(_httpClientFactory.CreateClient()));
        _kernel.ImportPluginFromObject(new UniversityFinderPlugin(_httpClientFactory.CreateClient()));

        var jsonSerializerOptions = new JsonSerializerOptions { WriteIndented = true };
        var exit = false;
        while (!exit)
        {
            Console.WriteLine("How can I help:");
            var ask = Console.ReadLine();

            var planner = new HandlebarsPlanner(new HandlebarsPlannerOptions() { AllowLoops = true });

            try
            {
                var plan = await planner.CreatePlanAsync(_kernel, ask!);
                Console.WriteLine("Plan:\n");
                Console.WriteLine(
                    JsonSerializer.Serialize(plan, jsonSerializerOptions));

                var result = await plan.InvokeAsync(_kernel, cancellationToken: cancellationToken);
                Console.WriteLine("Plan results:\n");
                Console.WriteLine(result);
            }
            catch (PlanCreationException e)
            {
                var errorDetails = e.InnerException?.Message;
                var promptDetails = e.CreatePlanPrompt;
                var modelResults = e?.ModelResults?.Content; // Proposed plan
                Console.WriteLine($"Error: {errorDetails}\n Prompt: {promptDetails}\n ModelResults: {modelResults}");
            }

            Console.WriteLine("\n\nDo you want to continue? (Y/N)");
            var response = Console.ReadLine();
            exit = response?.ToUpper() != "Y";
        }
    }

    private GraphServiceClient GetGraphServiceClient()
    {
        var scopes = new[] { "User.Read" };
        var clientId = _configuration.GetValue<string>("AzureAd:ClientId");
        var tenantId = _configuration.GetValue<string>("AzureAd:TenantId");

        var options = new InteractiveBrowserCredentialOptions
        {
            TenantId = tenantId,
            ClientId = clientId,
            RedirectUri = new Uri("http://localhost"),
        };

        var interactiveCredential = new InteractiveBrowserCredential(options);
        var graphClient = new GraphServiceClient(interactiveCredential, scopes);

        return graphClient;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogWarning("HostedService Stopped");
        return Task.CompletedTask;
    }
}
