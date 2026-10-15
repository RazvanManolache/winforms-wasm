using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using WfWasm.CanvasHost;
using System.Net.Http.Json;

AppContext.SetSwitch("System.Windows.Forms.ServicePointManagerCheckCrl", false);

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
var sceneState = new SceneState();
using (var catalogHttp = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) })
{
    sceneState.LimitToAvailableApps(await catalogHttp.GetFromJsonAsync<string[]>("api/apps") ?? []);
}
builder.Services.AddSingleton(sceneState);

await builder.Build().RunAsync();
