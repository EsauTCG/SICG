using Microsoft.EntityFrameworkCore;
using Plataforma_CG.Data;
using System.Net.Http;

public class CallMeBotService
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private readonly AppDbContext _db;
    private readonly Dictionary<string, DateTime> _ultimoEnvioPorClave = new();
    private readonly TimeSpan _cooldownDuplicado;
    private int _indexActual;

    public CallMeBotService(AppDbContext db, TimeSpan cooldownDuplicado)
    {
        _db = db;
        _cooldownDuplicado = cooldownDuplicado;
    }

    public async Task<bool> EnviarPorEstatusAsync(
        string telefonoCliente,
        string estatus,
        string mensaje,
        CancellationToken cancellationToken = default)
    {
        var clave = $"{telefonoCliente}|{estatus}";

        if (_ultimoEnvioPorClave.TryGetValue(clave, out var ultimo) &&
            DateTime.Now - ultimo < _cooldownDuplicado)
        {
            Console.WriteLine("Se evita un envio duplicado rapido.");
            return false;
        }

        var cuentas = await _db.WhatsAppAPI
            .AsNoTracking()
            .Where(x => x.Activo)
            .OrderBy(x => x.OrdenRotacion)
            .ToListAsync(cancellationToken);

        if (!cuentas.Any())
        {
            Console.WriteLine("No hay registros activos en WhatsAppAPI.");
            return false;
        }

        var cuenta = cuentas[_indexActual % cuentas.Count];
        _indexActual = (_indexActual + 1) % cuentas.Count;

        var url = $"https://api.callmebot.com/whatsapp.php?phone={cuenta.Phone}" +
                  $"&text={Uri.EscapeDataString(mensaje)}&apikey={cuenta.ApiKey}";

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            var response = await Client.GetAsync(url, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Error HTTP: {(int)response.StatusCode} {response.StatusCode}");
                return false;
            }

            _ultimoEnvioPorClave[clave] = DateTime.Now;
            Console.WriteLine($"Mensaje enviado ({estatus}) usando {cuenta.Phone}.");
            return true;
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("Timeout al enviar mensaje por CallMeBot.");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al enviar: {ex.Message}");
            return false;
        }
    }
}
