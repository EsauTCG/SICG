using Microsoft.Data.SqlClient;
using Plataforma_CG.Models.Operaciones.Inyeccion;
using System.Data;
using System.Globalization;
using System.Text.Json;

namespace Plataforma_CG.AccesoDatos.Operaciones.Inyeccion
{
    public class AccesoRecetas
    {
        private readonly HttpClient _connRead;
        private readonly string _cadenaSqlInyecciones;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public AccesoRecetas(IConfiguration configuration)
        {
            _connRead = new InyeccionAPI(configuration).Client();
            _cadenaSqlInyecciones = ResolverCadenaSqlInyecciones(configuration);
        }

        public async Task<List<ProductoModel>> ListarProductos(string plan)
        {
            var response = await _connRead.GetAsync($"Receta/ListarPlantilla?plan={Uri.EscapeDataString(plan ?? string.Empty)}");
            response.EnsureSuccessStatusCode();

            return await JsonSerializer.DeserializeAsync<List<ProductoModel>>(
                await response.Content.ReadAsStreamAsync(),
                _jsonOptions) ?? [];
        }

        public async Task<RecetaModel> Receta(string sku)
        {
            try
            {
                var response = await _connRead.GetAsync($"Receta/ConsultarReceta?sku={Uri.EscapeDataString(sku ?? string.Empty)}");
                response.EnsureSuccessStatusCode();
                string responseJson = await response.Content.ReadAsStringAsync();

                return JsonSerializer.Deserialize<RecetaModel>(responseJson, _jsonOptions) ?? new RecetaModel();
            }
            catch (HttpRequestException)
            {
                return new RecetaModel();
            }
        }

        public async Task<List<TaraModel>> Taras()
        {
            var response = await _connRead.GetAsync("Receta/ListarTara");
            response.EnsureSuccessStatusCode();

            return await JsonSerializer.DeserializeAsync<List<TaraModel>>(
                await response.Content.ReadAsStreamAsync(),
                _jsonOptions) ?? [];
        }

        public async Task<EntradaModel> InsertarEntrada(EntradaModel model)
        {
            ValidarEntrada(model);

            DateTime fechaCaptura = DateTime.Now;

            await using var connection = new SqlConnection(_cadenaSqlInyecciones);
            await connection.OpenAsync();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            try
            {
                const string insertSql = """
                    INSERT INTO [dbo].[Entradas]
                    (
                        [SKU], [fk_Inyectora], [Porcentaje], [ModoInyeccion],
                        [Presion], [Velocidad], [Altura], [Avance], [Bascula],
                        [FechaHora], [TipoPeso], [Autoriza], [Peso], [fk_Lote],
                        [Tara], [Plantilla], [UsSIGO]
                    )
                    OUTPUT INSERTED.[Id]
                    VALUES
                    (
                        @SKU, @FkInyectora, @Porcentaje, @ModoInyeccion,
                        @Presion, @Velocidad, @Altura, @Avance, @Bascula,
                        @FechaHora, @TipoPeso, @Autoriza, @Peso, @FkLote,
                        @Tara, @Plantilla, @UsSIGO
                    );
                    """;

                await using var insert = new SqlCommand(insertSql, connection, transaction);
                AgregarParametrosEntrada(insert, model, fechaCaptura);

                object? idResult = await insert.ExecuteScalarAsync();
                int id = Convert.ToInt32(idResult, CultureInfo.InvariantCulture);

                if (id <= 0)
                    throw new InvalidOperationException("SQL Server no devolvió un Id válido para la captura.");

                // Conserva el contrato histórico de la API: INY-{LoteId}{yyMMdd}{Id}.
                string folio = $"INY-{model.fk_Lote}{fechaCaptura:yyMMdd}{id}";

                const string updateFolioSql = """
                    UPDATE [dbo].[Entradas]
                    SET [Folio] = @Folio
                    WHERE [Id] = @Id;
                    """;

                await using var updateFolio = new SqlCommand(updateFolioSql, connection, transaction);
                updateFolio.Parameters.Add("@Folio", SqlDbType.NVarChar, 70).Value = folio;
                updateFolio.Parameters.Add("@Id", SqlDbType.Int).Value = id;

                if (await updateFolio.ExecuteNonQueryAsync() != 1)
                    throw new InvalidOperationException("No fue posible asignar el folio a la captura.");

                await transaction.CommitAsync();

                model.Id = id;
                model.Folio = folio;
                model.FechaHora = fechaCaptura.ToString("O", CultureInfo.InvariantCulture);
                return model;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<EntradaModel?> ConsultarEntrada(int id)
        {
            if (id <= 0)
                return null;

            const string selectSql = """
                SELECT
                    [Id], [SKU], [fk_Inyectora], [Porcentaje], [ModoInyeccion],
                    [Presion], [Velocidad], [Altura], [Avance], [Bascula],
                    [FechaHora], [TipoPeso], [Autoriza], [Peso], [Tara],
                    [fk_Lote], [Plantilla], [UsSIGO], [Folio]
                FROM [dbo].[Entradas]
                WHERE [Id] = @Id;
                """;

            await using var connection = new SqlConnection(_cadenaSqlInyecciones);
            await connection.OpenAsync();
            await using var command = new SqlCommand(selectSql, connection);
            command.Parameters.Add("@Id", SqlDbType.Int).Value = id;

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);
            if (!await reader.ReadAsync())
                return null;

            return new EntradaModel
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                SKU = GetString(reader, "SKU"),
                fk_Inyectora = GetInt32(reader, "fk_Inyectora"),
                Porcentaje = GetInt32(reader, "Porcentaje"),
                ModoInyeccion = GetInt32(reader, "ModoInyeccion"),
                Presion = GetDecimal(reader, "Presion"),
                Velocidad = GetInt32(reader, "Velocidad"),
                Altura = GetInt32(reader, "Altura"),
                Avance = GetString(reader, "Avance"),
                Bascula = GetString(reader, "Bascula"),
                FechaHora = GetDateTime(reader, "FechaHora")?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                TipoPeso = GetString(reader, "TipoPeso"),
                Autoriza = GetInt64(reader, "Autoriza"),
                Peso = GetDecimal(reader, "Peso"),
                Tara = GetDecimal(reader, "Tara"),
                fk_Lote = GetInt64(reader, "fk_Lote"),
                Plantilla = GetString(reader, "Plantilla"),
                UsSIGO = GetString(reader, "UsSIGO"),
                Folio = GetString(reader, "Folio")
            };
        }

        private static string ResolverCadenaSqlInyecciones(IConfiguration configuration)
        {
            string? connectionStringName = configuration["InyeccionesSql:ConnectionStringName"];
            if (string.IsNullOrWhiteSpace(connectionStringName))
            {
                throw new InvalidOperationException(
                    "Falta configurar InyeccionesSql:ConnectionStringName en appsettings.json.");
            }

            string? connectionString = configuration.GetConnectionString(connectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"No existe ConnectionStrings:{connectionStringName} para guardar capturas de Inyecciones.");
            }

            var builder = new SqlConnectionStringBuilder(connectionString);
            string? database = configuration["InyeccionesSql:Database"];
            if (!string.IsNullOrWhiteSpace(database))
                builder.InitialCatalog = database.Trim();

            builder.ApplicationName = "SIGO-Inyecciones";
            return builder.ConnectionString;
        }

        private static void ValidarEntrada(EntradaModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            model.SKU = (model.SKU ?? string.Empty).Trim();
            model.Avance = (model.Avance ?? string.Empty).Trim();
            model.Bascula = (model.Bascula ?? string.Empty).Trim();
            model.TipoPeso = (model.TipoPeso ?? string.Empty).Trim();
            model.Plantilla = (model.Plantilla ?? string.Empty).Trim();
            model.UsSIGO = (model.UsSIGO ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(model.SKU))
                throw new ArgumentException("El SKU es obligatorio.");
            if (model.SKU.Length > 20)
                throw new ArgumentException("El SKU no puede exceder 20 caracteres.");
            if (model.fk_Lote <= 0)
                throw new ArgumentException("Debe seleccionar un lote válido.");
            if (model.Peso <= 0)
                throw new ArgumentException("El peso neto debe ser mayor a cero.");
            if (model.Tara < 0)
                throw new ArgumentException("La tara no puede ser negativa.");
            if (model.Porcentaje is < 0 or > 100)
                throw new ArgumentException("El porcentaje de inyección debe estar entre 0 y 100.");
            if (model.Presion < 0 || model.Velocidad < 0 || model.Altura < 0)
                throw new ArgumentException("Los parámetros de la receta no pueden ser negativos.");
            if (model.TipoPeso is not ("Man" or "Aut"))
                throw new ArgumentException("El tipo de peso debe ser manual o automático.");
            if (model.TipoPeso == "Man" && model.Autoriza <= 0)
                throw new ArgumentException("Una captura manual requiere el usuario que la autorizó.");
            if (model.Avance.Length > 20)
                throw new ArgumentException("El avance no puede exceder 20 caracteres.");
            if (model.Bascula.Length > 60)
                throw new ArgumentException("La báscula no puede exceder 60 caracteres.");
            if (model.Plantilla.Length > 12)
                throw new ArgumentException("La plantilla no puede exceder 12 caracteres.");
        }

        private static void AgregarParametrosEntrada(SqlCommand command, EntradaModel model, DateTime fechaCaptura)
        {
            command.Parameters.Add("@SKU", SqlDbType.NVarChar, 20).Value = model.SKU;
            command.Parameters.Add("@FkInyectora", SqlDbType.Int).Value = model.fk_Inyectora;
            command.Parameters.Add("@Porcentaje", SqlDbType.Int).Value = model.Porcentaje;
            command.Parameters.Add("@ModoInyeccion", SqlDbType.Int).Value = model.ModoInyeccion;
            AddDecimal(command, "@Presion", model.Presion, 4);
            command.Parameters.Add("@Velocidad", SqlDbType.Int).Value = model.Velocidad;
            command.Parameters.Add("@Altura", SqlDbType.Int).Value = model.Altura;
            command.Parameters.Add("@Avance", SqlDbType.NVarChar, 20).Value = DbValue(model.Avance);
            command.Parameters.Add("@Bascula", SqlDbType.NVarChar, 60).Value = DbValue(model.Bascula);
            command.Parameters.Add("@FechaHora", SqlDbType.DateTime).Value = fechaCaptura;
            command.Parameters.Add("@TipoPeso", SqlDbType.NVarChar, 8).Value = model.TipoPeso;
            command.Parameters.Add("@Autoriza", SqlDbType.BigInt).Value = model.Autoriza;
            AddDecimal(command, "@Peso", model.Peso, 2);
            command.Parameters.Add("@FkLote", SqlDbType.BigInt).Value = model.fk_Lote;
            AddDecimal(command, "@Tara", model.Tara, 2);
            command.Parameters.Add("@Plantilla", SqlDbType.NVarChar, 12).Value = DbValue(model.Plantilla);
            command.Parameters.Add("@UsSIGO", SqlDbType.NVarChar, -1).Value = DbValue(model.UsSIGO);
        }

        private static void AddDecimal(SqlCommand command, string name, decimal value, byte scale)
        {
            var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
            parameter.Precision = 18;
            parameter.Scale = scale;
            parameter.Value = decimal.Round(value, scale, MidpointRounding.AwayFromZero);
        }

        private static object DbValue(string? value) =>
            string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

        private static string GetString(SqlDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }

        private static int GetInt32(SqlDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        private static long GetInt64(SqlDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        private static decimal GetDecimal(SqlDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? 0 : Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        private static DateTime? GetDateTime(SqlDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
        }
    }
}
