using System.Net.Sockets;
using System.Text;

namespace Plataforma_CG.Services
{
    public class BasculaService
    {
        public async Task<string> Bascula(string ip, string comando)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return "Error";

            comando = string.IsNullOrWhiteSpace(comando) ? "P" : comando.Trim();

            using var client = new TcpClient();

            try
            {
                // Timeout real de conexión
                using var ctsConnect = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
                await client.ConnectAsync(ip, 5000, ctsConnect.Token);

                using var stream = client.GetStream();

                // Timeout de lectura / escritura
                using var ctsIo = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));

                byte[] data = Encoding.ASCII.GetBytes(comando + "\r\n");
                await stream.WriteAsync(data, 0, data.Length, ctsIo.Token);
                await stream.FlushAsync(ctsIo.Token);

                byte[] buffer = new byte[512];
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ctsIo.Token);

                if (bytesRead <= 0)
                    return "Error";

                string respuesta = Encoding.ASCII.GetString(buffer, 0, bytesRead);

                respuesta = respuesta
                    .Replace("kg", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("\r", "")
                    .Replace("\n", "")
                    .Trim();

                // Limpieza opcional: deja solo números válidos tipo 12.345 o 12,345
                respuesta = respuesta.Replace(" ", "");

                return respuesta;
            }
            catch (OperationCanceledException)
            {
                return "Error";
            }
            catch
            {
                return "Error";
            }
            finally
            {
                try { client.Close(); } catch { }
            }
        }
    }
}