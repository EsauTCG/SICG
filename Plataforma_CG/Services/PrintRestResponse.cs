using System.Text.Json;

public class PrintRestResponse
{
    public int HttpStatus { get; set; }
    public string Raw { get; set; } = "";

    public string Mensaje { get; set; } = "";
    public int? Estado { get; set; }
    public int? Estatus { get; set; }

    public static PrintRestResponse FromRaw(int httpStatus, string raw)
    {
        var r = new PrintRestResponse { HttpStatus = httpStatus, Raw = raw ?? "" };

        try
        {
            using var doc = JsonDocument.Parse(r.Raw);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("Mensaje", out var m)) r.Mensaje = m.GetString() ?? "";
                if (root.TryGetProperty("Estado", out var e) && e.TryGetInt32(out var ei)) r.Estado = ei;
                if (root.TryGetProperty("Estatus", out var es) && es.TryGetInt32(out var esi)) r.Estatus = esi;
            }
        }
        catch
        {
            // Si el servicio responde HTML o algo no-JSON, se queda en Raw.
        }

        return r;
    }
}
