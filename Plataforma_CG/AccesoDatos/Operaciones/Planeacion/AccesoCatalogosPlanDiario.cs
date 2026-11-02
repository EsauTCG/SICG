using System.Data.SqlClient;
using Plataforma_CG.Models.Operaciones.Planeacion.Diaria;
using Plataforma_CG.Models.Operaciones.Planeacion.Extra;
using System.Data;

namespace Plataforma_CG.AccesoDatos.Operaciones.Planeacion;

// Lecturas por conjunto: una conexión por catálogo, sin caché entre aperturas.
internal sealed class AccesoCatalogosPlanDiario(string cadenaSigo, string cadenaTif)
{
    public async Task<List<CatalogoProductoDia>> ObtenerAsync(List<string> productos, CancellationToken cancellationToken)
    {
        var skus = productos.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (skus.Count > 1000 || skus.Any(s => s.Length > 50))
            throw new ArgumentException("La lista de productos del plan diario no es válida.");
        if (skus.Count == 0) return new();
        var resultado = skus.ToDictionary(s => s, s => new CatalogoProductoDia { ProductoCodigo = s }, StringComparer.OrdinalIgnoreCase);
        await Task.WhenAll(CargarConversiones(), CargarProceso());
        return skus.Select(s => resultado[s]).ToList();

        SqlCommand CrearComando(SqlConnection connection, string query)
        {
            var command = new SqlCommand(query.Replace("{skus}", string.Join(",", skus.Select((_, i) => "@sku" + i))), connection);
            for (var i = 0; i < skus.Count; i++)
                command.Parameters.Add("@sku" + i, SqlDbType.NVarChar, 50).Value = skus[i];
            return command;
        }

        async Task CargarConversiones()
        {
            using var connection = new SqlConnection(cadenaSigo);
            using var command = CrearComando(connection, @"
SELECT a.SkuOrigen,a.SkuDestino,b.ProductoNombre
FROM dbo.SkuConversion a
INNER JOIN dbo.ArticuloSap b ON b.ProductoCodigo=a.SkuDestino
WHERE a.SkuOrigen IN ({skus}) AND a.Activo=1;");
            await connection.OpenAsync(cancellationToken);
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var origen = Convert.ToString(reader["SkuOrigen"])!;
                if (resultado.TryGetValue(origen, out var item)) item.Conversiones.Add(new()
                {
                    SkuOrigen = origen, SkuDestino = Convert.ToString(reader["SkuDestino"]),
                    ProductoNombre = Convert.ToString(reader["ProductoNombre"])
                });
            }
        }

        async Task CargarProceso()
        {
            using var connection = new SqlConnection(cadenaTif);
            // Mismas relaciones y reglas de los endpoints individuales, dos resultados en un viaje.
            using var command = CrearComando(connection, @"
SELECT a.ArticuloId AS SKU,a.Nombre,ISNULL(b.Porcentaje,0) AS Porcentaje
FROM TIF_CommerciaNet.dbo.Articulo a
LEFT JOIN TIF_Inyeccion.dbo.Recetas b ON b.SKU=a.ArticuloId
WHERE a.ArticuloId IN ({skus});
SELECT b.ArticuloId,b.Etiquetacion,a.Nombre,a.INTERFACE AS DiasCaducidad
FROM TIF_CommerciaNet.dbo.Colector a
INNER JOIN TIF_CommerciaNet.dbo.Articulo b ON b.Etiquetacion=a.ColectorId
WHERE a.SistemaId='ETI' AND b.ArticuloId IN ({skus});");
            await connection.OpenAsync(cancellationToken);
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var sku = Convert.ToString(reader["SKU"])!;
                if (resultado.TryGetValue(sku, out var item))
                {
                    var porcentaje = Convert.ToInt32(reader["Porcentaje"]);
                    item.Iny = new PlanInyModel
                    {
                        SKU = sku, Nombre = Convert.ToString(reader["Nombre"]), Porcentaje = porcentaje,
                        Tipo = porcentaje > 0 ? "Mejorado" : "Natural"
                    };
                }
            }
            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var sku = Convert.ToString(reader["ArticuloId"])!;
                if (resultado.TryGetValue(sku, out var item)) item.Etiq = new PlanEtiModel
                {
                    Etiquetacion = Convert.ToInt32(reader["Etiquetacion"]), Nombre = Convert.ToString(reader["Nombre"]),
                    DiasCaducidad = Convert.ToString(reader["DiasCaducidad"])
                };
            }
        }
    }
}
