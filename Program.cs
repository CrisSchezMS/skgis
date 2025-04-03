using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.ChatCompletion;
using System;
using System.Threading.Tasks;
using System.Globalization;

string connectionString = "Host=yourhost;Database=nyc;Port=5432;Username=username;Password='yourpass';";

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
// Inicializa el kernel
var kernel = Kernel.CreateBuilder()
    .AddAzureOpenAIChatCompletion(
        deploymentName: "gpt-4o",
        endpoint: "endpoint",
        apiKey: "yourkey")
    .Build();

// En el main:
var gisPlugin = new GisPlugin(connectionString);

kernel.ImportPluginFromObject(new GisPlugin(connectionString), "GIS");
kernel.ImportPluginFromObject(new SqlPlugin(connectionString), "SQL");


// 🔍 Función semántica para decidir qué función usar y extraer parámetros
var routingPrompt = @"
Decide qué función usar en base a la pregunta del usuario.
Si la pregunta menciona coordenadas (latitud y longitud), usa BuscarLugaresCercanosAsync.
Si menciona una estación de metro y airbnb o alojamiento, usa BuscarAirbnbCercanos.
Si el usuario quiere ver un mapa de alojamientos, usa GenerarMapaHTMLAsync.

Devuelve un JSON con el formato:
{
  ""function"": ""NombreDeFuncion"",
  ""args"": {
    ...
  }
}

Ejemplo:
Pregunta: ¿Qué alojamientos hay cerca de la estación de metro 14 St a 500 metros?
Salida:
{
  ""function"": ""BuscarAirbnbCercanos"",
  ""args"": {
    ""metro"": ""14 St"",
    ""radio"": 500
  }
}

Pregunta: ¿Qué estaciones hay cerca de 40.71427, -74.00597 en 100 metros?
Salida:
{
  ""function"": ""BuscarLugaresCercanosAsync"",
  ""args"": {
    ""lat"": 40.71427,
    ""lon"": -74.00597,
    ""radio"": 100
  }
}

Pregunta: Muéstrame el mapa con los alojamientos disponibles
Salida:
{
  ""function"": ""GenerarMapaHTMLAsync"",
  ""args"": {}
}

IMPORTANTE: Devuelve solo JSON válido. Usa comillas dobles ("") para claves y valores.

Pregunta: {{$input}}
Salida:
";

var interpretarFuncion = kernel.CreateFunctionFromPrompt(routingPrompt, functionName: "InterpretarPregunta");

app.MapGet("/", () => Results.Redirect("/mapa.html"));
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/preguntar", async (HttpContext context) => {
    var pregunta = context.Request.Query["q"].ToString();
    var interpretacion = await kernel.InvokeAsync(interpretarFuncion, new() { ["input"] = pregunta });

    using var jsonDoc = System.Text.Json.JsonDocument.Parse(interpretacion.ToString());
    var root = jsonDoc.RootElement;
    var funcion = root.GetProperty("function").GetString();
    var args = root.GetProperty("args");

    var kernelArgs = new KernelArguments();
    foreach (var prop in args.EnumerateObject())
    {
        if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Number)
            kernelArgs[prop.Name] = prop.Value.GetDouble();
        else
            kernelArgs[prop.Name] = prop.Value.GetString();
    }

    var resultado = await kernel.InvokeAsync(kernel.Plugins["GIS"][funcion], kernelArgs);

    // Si es HTML, devolverlo como tal
    if (funcion == "GenerarMapaHTMLAsync")
        return Results.Content(resultado.ToString(), "text/html");

    return Results.Ok(resultado.ToString());
});
app.MapGet("/api/airbnb", () => Results.Json(GisPlugin.UltimosResultados));
app.MapGet("/mapa", async () =>
{
    var html = await gisPlugin.GenerarMapaHTMLAsync();
    return Results.Content(html, "text/html");
});
app.Run();

