using System.ComponentModel;
using Microsoft.SemanticKernel;
using Npgsql;

public class GisPlugin
{
    private readonly string _connectionString;
    public static List<AirbnbResult> UltimosResultados = new();
    public GisPlugin(string connectionString)
    {
        _connectionString = connectionString;
    }

    [KernelFunction("BuscarLugaresCercanosAsync")]
    public async Task<string> BuscarLugaresCercanosAsync(double lat, double lon, double radio)
    {
        var resultados = new List<string>();

        using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        string sql = @"
            SELECT name
            FROM nyc_subway_stations
            WHERE ST_DWithin(
                ST_Transform(geom, 4326)::geography,
                ST_SetSRID(ST_Point(@lon, @lat), 4326)::geography,
                @radio
            );
        ";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("lat", lat);
        cmd.Parameters.AddWithValue("lon", lon);
        cmd.Parameters.AddWithValue("radio", radio);


        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            resultados.Add(reader.GetString(0));
        }

        return resultados.Count > 0
            ? $"Se encontraron: {string.Join(", ", resultados)}"
            : "No se encontraron lugares con esos criterios.";
    }

    [KernelFunction("BuscarAirbnbCercanos")]
    public async Task<string> BuscarAirbnbCercanos(string metro, double radio)
    {
        var resultados = new List<AirbnbResult>();
        using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        string sql = @"
            SELECT host_id, host_name,
                   ST_Y(listing_geom::geometry) AS lat,
                   ST_X(listing_geom::geometry) AS lon
            FROM nyc_listings_bnb AS l
            JOIN (
                SELECT ST_Transform(ST_Buffer(geom, @radio), 4326) AS geo_fence
                FROM nyc_subway_stations 
                WHERE name = @metro
            ) AS g
            ON ST_Contains(g.geo_fence, l.listing_geom);";

        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("metro", metro);
        cmd.Parameters.AddWithValue("radio", radio);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            resultados.Add(new AirbnbResult(
                reader["host_id"].ToString(),
                reader["host_name"].ToString(),
                Convert.ToDouble(reader["lat"]),
                Convert.ToDouble(reader["lon"])
            ));
        }

        // Guardar en cache para el mapa dinámico
        UltimosResultados = resultados;

        return resultados.Count > 0
            ? $"Se encontraron: {resultados.Count} alojamientos."
            : "No se encontraron alojamientos cercanos.";
    }



   


    [KernelFunction("GenerarMapaHTMLAsync")]
    public async Task<string> GenerarMapaHTMLAsync()
    {
        var puntos = await ObtenerAirbnbParaMapaAsync();

        var markers = string.Join("\n", puntos.Select(p =>
            $"L.marker([{p.Lat}, {p.Lon}]).addTo(map).bindPopup(\"{p.HostName.Replace("\"", "'")}\");"));

        var html = $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'>
  <title>Mapa de Airbnb</title>
  <link rel='stylesheet' href='https://unpkg.com/leaflet/dist/leaflet.css' />
  <style>#map {{ height: 100vh; margin: 0; }}</style>
</head>
<body>
  <div id='map'></div>
  <script src='https://unpkg.com/leaflet/dist/leaflet.js'></script>
  <script>
    var map = L.map('map').setView([40.71427, -74.00597], 13);
    L.tileLayer('https://{{s}}.tile.openstreetmap.org/{{z}}/{{x}}/{{y}}.png').addTo(map);
    {markers}
  </script>
</body>
</html>";

        return html;
    }


   [KernelFunction("ObtenerAirbnbParaMapaAsync")] 
    public async Task<List<AirbnbResult>> ObtenerAirbnbParaMapaAsync()
        => UltimosResultados;
}

public record AirbnbResult(string HostId, string HostName, double Lat, double Lon);