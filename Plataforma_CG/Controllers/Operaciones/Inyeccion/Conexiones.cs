using Plataforma_CG.Models.Operaciones.Inyeccion;
using System.Net.Sockets;
using System.Text;
using System.Drawing;

namespace Plataforma_CG.Controllers.Operaciones.Inyeccion
{
    public class Conexiones
    {
        public (bool ok, string mensaje) Impresion(
            int tipopes,
            EntradaModel model,
            string ip,
            string lote,
            string prod
        )
        {
            try
            {
                string rutaLogo = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "LOGO_CARNESG.JPG");

                if (!File.Exists(rutaLogo))
                {
                    return (false, $"No se encontró el logo en la ruta: {rutaLogo}");
                }

                string gfaLogo = ConvertirImagenAGFA(rutaLogo, 400, 110);

                string pes = model.TipoPeso == "Man"
                    ? "Manual"
                    : model.TipoPeso == "Aut"
                        ? "Automatico"
                        : "Modificacion";

                string productoSeguro = LimpiarTextoZpl(prod);
                string loteSeguro = LimpiarTextoZpl(lote);
                string skuSeguro = LimpiarTextoZpl(model.SKU ?? "");
                string folioSeguro = LimpiarTextoZpl(model.Folio ?? "");

                string datos = $@"
^XA
^CI28
^PW812
^LL1218
^LH0,0

^FO206,30
{gfaLogo}

^FO40,220^A0N,40,40^FDProduccionId:^FS
^FO280,220^A0N,40,40^FD{model.Id}^FS

^FO40,280^A0N,40,40^FDLote:^FS
^FO280,280^A0N,40,40^FD{loteSeguro}^FS

^FO40,340^A0N,40,40^FDSKU:^FS
^FO280,340^A0N,40,40^FD{skuSeguro}^FS

^FO40,400^A0N,40,40^FDProducto:^FS
^FO280,400^A0N,40,40^FD{productoSeguro}^FS

^FO40,520^GB732,3,3^FS

^FO40,560^A0N,40,40^FDPeso Neto:^FS
^FO340,560^A0N,40,40^FD{model.Peso:F3} Kg^FS

^FO40,620^A0N,40,40^FDTara:^FS
^FO340,620^A0N,40,40^FD{model.Tara:F3} Kg^FS

^FO40,680^A0N,40,40^FDPesaje: {pes}^FS

^FO40,740^GB732,3,3^FS

^FO28,780
^BY3,3,100
^BCN,120,Y,N,N
^FD{folioSeguro}^FS

^XZ";

                return Imprimir(ip, 9100, datos);
            }
            catch (Exception ex)
            {
                return (false, $"Excepción al generar impresión: {ex.Message}");
            }
        }

        private static string LimpiarTextoZpl(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
                return "";

            return texto
                .Replace("^", "")
                .Replace("~", "")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
        }

        private static string ConvertirImagenAGFA(string ruta, int anchoPx, int altoPx)
        {
            using var original = new Bitmap(ruta);
            using var imagen = new Bitmap(original, new Size(anchoPx, altoPx));

            int bytesPorFila = (anchoPx + 7) / 8;
            var sb = new StringBuilder();

            for (int y = 0; y < imagen.Height; y++)
            {
                byte actual = 0;
                int bit = 7;

                for (int x = 0; x < imagen.Width; x++)
                {
                    var pixel = imagen.GetPixel(x, y);
                    int gris = (pixel.R + pixel.G + pixel.B) / 3;

                    if (gris < 140)
                        actual |= (byte)(1 << bit);

                    bit--;

                    if (bit < 0)
                    {
                        sb.Append(actual.ToString("X2"));
                        actual = 0;
                        bit = 7;
                    }
                }

                if (bit != 7)
                    sb.Append(actual.ToString("X2"));
            }

            int totalBytes = bytesPorFila * altoPx;
            return $"^GFA,{totalBytes},{totalBytes},{bytesPorFila},{sb}";
        }

        private (bool ok, string mensaje) Imprimir(string printerIp, int port, string datos)
        {
            int intentos = 0;
            string ultimoError = "";

            while (intentos < 2)
            {
                try
                {
                    using var client = new TcpClient();

                    // ⬇ más tolerante
                    var connectTask = client.ConnectAsync(printerIp, port);
                    if (!connectTask.Wait(TimeSpan.FromSeconds(8)))
                    {
                        throw new Exception("Timeout al conectar con la impresora");
                    }

                    client.ReceiveTimeout = 8500;
                    client.SendTimeout = 8500;
                    client.NoDelay = true;

                    using var stream = client.GetStream();
                    byte[] data = Encoding.ASCII.GetBytes(datos);

                    stream.Write(data, 0, data.Length);
                    stream.Flush();

                    // pequeño respiro para impresoras lentas
                    Thread.Sleep(300);

                    return (true, "Etiqueta enviada correctamente.");
                }
                catch (Exception ex)
                {
                    ultimoError = ex.Message;
                    intentos++;

                    // pequeño respiro entre intentos
                    Thread.Sleep(500);
                }
            }

            return (false, $"No se pudo enviar la etiqueta a {printerIp}:{port}. Detalle: {ultimoError}");
        }
    }
}