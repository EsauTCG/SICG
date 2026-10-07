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

        public async Task<EntradaModel> InsertarEntrada(EntradaModel model, Guid capturaGuid)
        {
            ValidarEntrada(model);
            if (capturaGuid == Guid.Empty)
                throw new ArgumentException("La captura no tiene un identificador único válido.");

            DateTime fechaCaptura = DateTime.Now;

            await using var connection = new SqlConnection(_cadenaSqlInyecciones);
            await connection.OpenAsync();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                const string existingSql = """
                    SELECT TOP (1) [Id]
                    FROM [dbo].[Entradas] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [CapturaGuid] = @CapturaGuid;
                    """;

                await using (var existing = new SqlCommand(existingSql, connection, transaction))
                {
                    existing.Parameters.Add("@CapturaGuid", SqlDbType.UniqueIdentifier).Value = capturaGuid;
                    object? existingId = await existing.ExecuteScalarAsync();

                    if (existingId != null && existingId != DBNull.Value)
                    {
                        int idExistente = Convert.ToInt32(existingId, CultureInfo.InvariantCulture);
                        await transaction.CommitAsync();
                        return await ConsultarEntrada(idExistente)
                            ?? throw new InvalidOperationException("La captura ya existía, pero no pudo recuperarse.");
                    }
                }

                const string insertSql = """
                    INSERT INTO [dbo].[Entradas]
                    (
                        [SKU], [fk_Inyectora], [Porcentaje], [ModoInyeccion],
                        [Presion], [Velocidad], [Altura], [Avance], [Bascula],
                        [FechaHora], [TipoPeso], [Autoriza], [Peso], [fk_Lote],
                        [Tara], [Plantilla], [UsSIGO], [CapturaGuid]
                    )
                    OUTPUT INSERTED.[Id]
                    VALUES
                    (
                        @SKU, @FkInyectora, @Porcentaje, @ModoInyeccion,
                        @Presion, @Velocidad, @Altura, @Avance, @Bascula,
                        @FechaHora, @TipoPeso, @Autoriza, @Peso, @FkLote,
                        @Tara, @Plantilla, @UsSIGO, @CapturaGuid
                    );
                    """;

                await using var insert = new SqlCommand(insertSql, connection, transaction);
                AgregarParametrosEntrada(insert, model, fechaCaptura);
                insert.Parameters.Add("@CapturaGuid", SqlDbType.UniqueIdentifier).Value = capturaGuid;

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

        public async Task<long> RegistrarIntentoImpresion(
            ImpresionEntradaRequest solicitud,
            EntradaModel entradaPersistida,
            string usuario,
            string payloadZpl,
            string payloadSha256)
        {
            ArgumentNullException.ThrowIfNull(solicitud);
            ArgumentNullException.ThrowIfNull(entradaPersistida);

            const string insertSql = """
                INSERT INTO [dbo].[InyeccionImpresionLog]
                (
                    [SolicitudGuid], [CapturaGuid], [EntradaId], [Folio],
                    [EsReimpresion], [FechaSolicitudUtc], [Estado], [SKU],
                    [Producto], [LoteId], [Lote], [Plantilla], [Peso], [Tara],
                    [TipoPeso], [ImpresoraIp], [Usuario], [PayloadZpl], [PayloadSha256]
                )
                OUTPUT INSERTED.[Id]
                VALUES
                (
                    @SolicitudGuid, @CapturaGuid, @EntradaId, @Folio,
                    @EsReimpresion, SYSUTCDATETIME(), N'PENDIENTE', @SKU,
                    @Producto, @LoteId, @Lote, @Plantilla, @Peso, @Tara,
                    @TipoPeso, @ImpresoraIp, @Usuario, @PayloadZpl, @PayloadSha256
                );
                """;

            await using var connection = new SqlConnection(_cadenaSqlInyecciones);
            await connection.OpenAsync();
            await using var command = new SqlCommand(insertSql, connection);

            command.Parameters.Add("@SolicitudGuid", SqlDbType.UniqueIdentifier).Value = solicitud.SolicitudGuid;
            command.Parameters.Add("@CapturaGuid", SqlDbType.UniqueIdentifier).Value =
                solicitud.CapturaGuid == Guid.Empty ? DBNull.Value : solicitud.CapturaGuid;
            command.Parameters.Add("@EntradaId", SqlDbType.Int).Value = entradaPersistida.Id;
            command.Parameters.Add("@Folio", SqlDbType.NVarChar, 70).Value = DbValue(entradaPersistida.Folio);
            command.Parameters.Add("@EsReimpresion", SqlDbType.Bit).Value = solicitud.EsReimpresion;
            command.Parameters.Add("@SKU", SqlDbType.NVarChar, 20).Value = entradaPersistida.SKU;
            command.Parameters.Add("@Producto", SqlDbType.NVarChar, 200).Value = solicitud.Producto;
            command.Parameters.Add("@LoteId", SqlDbType.BigInt).Value = entradaPersistida.fk_Lote;
            command.Parameters.Add("@Lote", SqlDbType.NVarChar, 120).Value = solicitud.Lote;
            command.Parameters.Add("@Plantilla", SqlDbType.NVarChar, 12).Value = DbValue(entradaPersistida.Plantilla);
            AddDecimal(command, "@Peso", entradaPersistida.Peso, 3);
            AddDecimal(command, "@Tara", entradaPersistida.Tara, 3);
            command.Parameters.Add("@TipoPeso", SqlDbType.NVarChar, 8).Value = DbValue(entradaPersistida.TipoPeso);
            command.Parameters.Add("@ImpresoraIp", SqlDbType.NVarChar, 64).Value = solicitud.IpImpresora;
            command.Parameters.Add("@Usuario", SqlDbType.NVarChar, 256).Value = DbValue(usuario);
            command.Parameters.Add("@PayloadZpl", SqlDbType.NVarChar, -1).Value = payloadZpl;
            command.Parameters.Add("@PayloadSha256", SqlDbType.Char, 64).Value = payloadSha256;

            object? result = await command.ExecuteScalarAsync();
            return Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }

        public async Task ActualizarResultadoImpresion(long logId, bool enviada, string mensaje)
        {
            const string updateSql = """
                UPDATE [dbo].[InyeccionImpresionLog]
                SET [Estado] = @Estado,
                    [Mensaje] = @Mensaje,
                    [FechaResultadoUtc] = SYSUTCDATETIME()
                WHERE [Id] = @Id;
                """;

            await using var connection = new SqlConnection(_cadenaSqlInyecciones);
            await connection.OpenAsync();
            await using var command = new SqlCommand(updateSql, connection);
            string mensajeSeguro = mensaje ?? string.Empty;
            command.Parameters.Add("@Estado", SqlDbType.NVarChar, 20).Value = enviada ? "ENVIADO" : "ERROR";
            command.Parameters.Add("@Mensaje", SqlDbType.NVarChar, 2000).Value =
                DbValue(mensajeSeguro.Length > 2000 ? mensajeSeguro[..2000] : mensajeSeguro);
            command.Parameters.Add("@Id", SqlDbType.BigInt).Value = logId;

            if (await command.ExecuteNonQueryAsync() != 1)
                throw new InvalidOperationException($"No fue posible actualizar la bitácora de impresión {logId}.");
        }

        public async Task<List<InyeccionImpresionLogModel>> ConsultarBitacoraImpresion(
            DateTime desdeUtc,
            DateTime hastaUtc,
            string? sku,
            string? producto,
            string? estado,
            int limite)
        {
            const string selectSql = """
                SELECT TOP (@Limite)
                    [Id], [SolicitudGuid], [CapturaGuid], [EntradaId], [Folio],
                    [EsReimpresion], [FechaSolicitudUtc], [FechaResultadoUtc], [Estado],
                    [SKU], [Producto], [LoteId], [Lote], [Plantilla], [Peso], [Tara],
                    [TipoPeso], [ImpresoraIp], [Usuario], [Mensaje], [PayloadSha256]
                FROM [dbo].[InyeccionImpresionLog]
                WHERE [FechaSolicitudUtc] >= @DesdeUtc
                  AND [FechaSolicitudUtc] < @HastaUtc
                  AND (@SKU = N'' OR [SKU] LIKE N'%' + @SKU + N'%')
                  AND (@Producto = N'' OR [Producto] LIKE N'%' + @Producto + N'%')
                  AND (@Estado = N'' OR [Estado] = @Estado)
                ORDER BY [FechaSolicitudUtc] DESC, [Id] DESC;
                """;

            await using var connection = new SqlConnection(_cadenaSqlInyecciones);
            await connection.OpenAsync();
            await using var command = new SqlCommand(selectSql, connection);
            command.Parameters.Add("@Limite", SqlDbType.Int).Value = Math.Clamp(limite, 1, 2000);
            command.Parameters.Add("@DesdeUtc", SqlDbType.DateTime2).Value = desdeUtc;
            command.Parameters.Add("@HastaUtc", SqlDbType.DateTime2).Value = hastaUtc;
            command.Parameters.Add("@SKU", SqlDbType.NVarChar, 20).Value = (sku ?? string.Empty).Trim();
            command.Parameters.Add("@Producto", SqlDbType.NVarChar, 200).Value = (producto ?? string.Empty).Trim();
            command.Parameters.Add("@Estado", SqlDbType.NVarChar, 20).Value = (estado ?? string.Empty).Trim().ToUpperInvariant();

            var resultado = new List<InyeccionImpresionLogModel>();
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                resultado.Add(new InyeccionImpresionLogModel
                {
                    Id = GetInt64(reader, "Id"),
                    SolicitudGuid = GetGuid(reader, "SolicitudGuid"),
                    CapturaGuid = GetGuid(reader, "CapturaGuid"),
                    EntradaId = GetInt32(reader, "EntradaId"),
                    Folio = GetString(reader, "Folio"),
                    EsReimpresion = GetBoolean(reader, "EsReimpresion"),
                    FechaSolicitudUtc = GetUtcDateTime(reader, "FechaSolicitudUtc") ?? DateTime.MinValue,
                    FechaResultadoUtc = GetUtcDateTime(reader, "FechaResultadoUtc"),
                    Estado = GetString(reader, "Estado"),
                    SKU = GetString(reader, "SKU"),
                    Producto = GetString(reader, "Producto"),
                    LoteId = GetInt64(reader, "LoteId"),
                    Lote = GetString(reader, "Lote"),
                    Plantilla = GetString(reader, "Plantilla"),
                    Peso = GetDecimal(reader, "Peso"),
                    Tara = GetDecimal(reader, "Tara"),
                    TipoPeso = GetString(reader, "TipoPeso"),
                    ImpresoraIp = GetString(reader, "ImpresoraIp"),
                    Usuario = GetString(reader, "Usuario"),
                    Mensaje = GetString(reader, "Mensaje"),
                    PayloadSha256 = GetString(reader, "PayloadSha256")
                });
            }

            return resultado;
        }

        public async Task<InyeccionImpresionLogModel?> ConsultarBitacoraImpresionPorId(long id)
        {
            if (id <= 0)
                return null;

            const string selectSql = """
                SELECT
                    [Id], [SolicitudGuid], [CapturaGuid], [EntradaId], [Folio],
                    [EsReimpresion], [FechaSolicitudUtc], [FechaResultadoUtc], [Estado],
                    [SKU], [Producto], [LoteId], [Lote], [Plantilla], [Peso], [Tara],
                    [TipoPeso], [ImpresoraIp], [Usuario], [Mensaje], [PayloadZpl], [PayloadSha256]
                FROM [dbo].[InyeccionImpresionLog]
                WHERE [Id] = @Id;
                """;

            await using var connection = new SqlConnection(_cadenaSqlInyecciones);
            await connection.OpenAsync();
            await using var command = new SqlCommand(selectSql, connection);
            command.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);
            if (!await reader.ReadAsync())
                return null;

            return new InyeccionImpresionLogModel
            {
                Id = GetInt64(reader, "Id"),
                SolicitudGuid = GetGuid(reader, "SolicitudGuid"),
                CapturaGuid = GetGuid(reader, "CapturaGuid"),
                EntradaId = GetInt32(reader, "EntradaId"),
                Folio = GetString(reader, "Folio"),
                EsReimpresion = GetBoolean(reader, "EsReimpresion"),
                FechaSolicitudUtc = GetUtcDateTime(reader, "FechaSolicitudUtc") ?? DateTime.MinValue,
                FechaResultadoUtc = GetUtcDateTime(reader, "FechaResultadoUtc"),
                Estado = GetString(reader, "Estado"),
                SKU = GetString(reader, "SKU"),
                Producto = GetString(reader, "Producto"),
                LoteId = GetInt64(reader, "LoteId"),
                Lote = GetString(reader, "Lote"),
                Plantilla = GetString(reader, "Plantilla"),
                Peso = GetDecimal(reader, "Peso"),
                Tara = GetDecimal(reader, "Tara"),
                TipoPeso = GetString(reader, "TipoPeso"),
                ImpresoraIp = GetString(reader, "ImpresoraIp"),
                Usuario = GetString(reader, "Usuario"),
                Mensaje = GetString(reader, "Mensaje"),
                PayloadZpl = GetString(reader, "PayloadZpl"),
                PayloadSha256 = GetString(reader, "PayloadSha256")
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

        private static DateTime? GetUtcDateTime(SqlDataReader reader, string column)
        {
            DateTime? value = GetDateTime(reader, column);
            return value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
        }

        private static Guid GetGuid(SqlDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? Guid.Empty : reader.GetGuid(ordinal);
        }

        private static bool GetBoolean(SqlDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            return !reader.IsDBNull(ordinal) && reader.GetBoolean(ordinal);
        }
    }
}
