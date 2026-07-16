using Plataforma_CG.Models;
using System.Net.Sockets;
using System.Text;
using System.Drawing;

namespace Plataforma_CG.Controllers.Comercial.Muestras
{
    public class Conexiones
    {
        public (bool ok, string mensaje) Impresion(
            EtiquetaMuestraPrintModel model,
            string ip
        )
        {
            try
            {
                string rutaLogo = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "logo_sigo_cg2_1.png");

                string gfaLogo = "";
                if (File.Exists(rutaLogo))
                {
                    gfaLogo = ConvertirImagenAGFA(rutaLogo, 420, 140);
                }

                string producto = LimpiarTextoZpl($"{model.Product} {model.Species}".Trim());
                string lote = LimpiarTextoZpl(model.Lote);
                string solicitud = LimpiarTextoZpl(model.SolicitudId);
                string vendedor = LimpiarTextoZpl(model.Seller);
                string cliente = LimpiarTextoZpl(model.Client);
                string skuReq = LimpiarTextoZpl(model.SkuRequerido);
                string skuTrab = LimpiarTextoZpl(model.SkuTrabajo);
                string fecha = LimpiarTextoZpl(model.FechaProduccion);
                string operario = LimpiarTextoZpl(model.Operario);
                string spec = LimpiarTextoZpl(model.Spec);
                string temp = LimpiarTextoZpl(model.Temperatura);

                string datos = $@"^XA
^CI28
^PW812
^LL1348
^LH0,0

{(string.IsNullOrEmpty(gfaLogo) ? "" : $@"^FO206,30
{gfaLogo}")}

^FX --- Cabecera  ---
^FO30,180^A0N,22,22^FDELABORADO POR: CARNES G S.A. DE C.V. TIF 776^FS
^FO30,210^A0N,22,22^FDCARRETERA ESTATAL N212 A SAN SEBASTIAN EL ALAMO KM 21+540^FS
^FO30,240^A0N,22,22^FDMARGEN DERECHO. SAN JUAN DE LOS LAGOS JAL CP 47000^FS

^FX --- Titulo de Producto ---
^FO30,310^A0N,28,28^FDPRODUCTO / CLASIFICACION:^FS
^FO30,350^FB750,2,0,L^A0N,45,45^FD{producto}^FS

^FX --- Solicitud, Vendedor y Cliente ---
^FO30,450^A0N,28,28^FDSolicitud: {solicitud}^FS
^FO400,450^A0N,28,28^FDVendedor: {vendedor}^FS
^FO30,500^A0N,28,28^FDCliente: {cliente}^FS

^FX --- Linea separadora gruesa ---
^FO30,560^GB740,4,4^FS

^FX --- Cuadricula de Detalles (Izquierda) ---
^FO30,610^A0N,35,35^FDLote: {lote}^FS
^FO30,670^A0N,28,28^FDSKU REQ: {skuReq}^FS

^FX --- SKU Trabajado con fondo invertido ---
^FO30,730^A0N,28,28^FDSKU TRAB: ^FS
^FO185,720^GB140,40,40^FS
^FO195,726^A0N,28,28^FR^FD{skuTrab}^FS

^FX --- Cuadricula de Detalles (Derecha) ---
^FO400,610^A0N,22,22^FDFecha Prod: {fecha}^FS
^FO400,650^A0N,22,22^FDOperario: {operario}^FS
^FO400,690^FB380,2,0,L^A0N,22,22^FDEsp: {spec}^FS

^FX --- Temperatura ---
^FO30,830^A0N,32,32^FDProducto {temp}^FS

^FX --- Codigo de Barras centrado abajo ---
^FO120,930^BY3^BCN,180,Y,N,N^FD{lote}^FS

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

                    if (pixel.A < 128)
                    {
                        bit--;
                        if (bit < 0)
                        {
                            sb.Append(actual.ToString("X2"));
                            actual = 0;
                            bit = 7;
                        }
                        continue;
                    }

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

                    Thread.Sleep(300);

                    return (true, "Etiqueta enviada correctamente.");
                }
                catch (Exception ex)
                {
                    ultimoError = ex.Message;
                    intentos++;
                    Thread.Sleep(500);
                }
            }

            return (false, $"No se pudo enviar la etiqueta a {printerIp}:{port}. Detalle: {ultimoError}");
        }
    }
}
