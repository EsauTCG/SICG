using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Plataforma_CG.Models;

namespace Plataforma_CG.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;

        public DatabaseService(IConfiguration config)
        {
            var db = config.GetSection("Database");
            _connectionString = new SqlConnectionStringBuilder
            {
                DataSource = db["Server"],
                InitialCatalog = db["Database"],
                UserID = db["User"],
                Password = db["Password"],
                TrustServerCertificate = bool.Parse(db["TrustServerCertificate"] ?? "true")
            }.ConnectionString;
        }

        public async Task GuardarUsuariosAsync(List<UsuarioAD> usuarios)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            string query = @"
                IF NOT EXISTS (SELECT 1 FROM UsuariosAD WHERE UsuarioAD = @usuarioAD)
                BEGIN
                    INSERT INTO UsuariosAD (UsuarioAD, Nombre, Puesto)
                    VALUES (@usuarioAD, @nombre, @puesto)
                END
                ELSE
                BEGIN
                    UPDATE UsuariosAD
                    SET Nombre = @nombre, Puesto = @puesto
                    WHERE UsuarioAD = @usuarioAD
                END";

            foreach (var u in usuarios)
            {
                using var cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@usuarioAD", u.UsuarioAd);
                cmd.Parameters.AddWithValue("@nombre", u.Nombre);
                cmd.Parameters.AddWithValue("@puesto", (object?)u.Puesto ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }
}