using Microsoft.Extensions.Configuration;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Plataforma_CG.Services
{
    /// <summary>
    /// Estado global en memoria del proceso automático SAP.
    /// El valor inicial se toma de IntegracionesSap:Automatico:Activo.
    /// </summary>
    public sealed class IntegracionSapAutomaticoState
    {
        private readonly object _sync = new();
        private readonly SemaphoreSlim _wakeSignal = new(0, 1);

        private bool _activo;
        private bool _procesando;
        private DateTime? _ultimoCambio;
        private DateTime? _ultimoInicioCiclo;
        private DateTime? _ultimoFinCiclo;
        private string _usuarioUltimoCambio = "CONFIGURACION";
        private string _ultimoMensaje = "Sin ciclos ejecutados.";

        public IntegracionSapAutomaticoState(IConfiguration configuration)
        {
            _activo = bool.TryParse(
                configuration["IntegracionesSap:Automatico:Activo"],
                out var activoConfigurado)
                && activoConfigurado;

            _ultimoCambio = DateTime.Now;
        }

        public IntegracionSapAutomaticoSnapshot ObtenerEstado()
        {
            lock (_sync)
            {
                return new IntegracionSapAutomaticoSnapshot
                {
                    Activo = _activo,
                    Procesando = _procesando,
                    UltimoCambio = _ultimoCambio,
                    UltimoInicioCiclo = _ultimoInicioCiclo,
                    UltimoFinCiclo = _ultimoFinCiclo,
                    UsuarioUltimoCambio = _usuarioUltimoCambio,
                    UltimoMensaje = _ultimoMensaje
                };
            }
        }

        public bool EstaActivo()
        {
            lock (_sync)
                return _activo;
        }

        public IntegracionSapAutomaticoSnapshot CambiarEstado(
            bool activo,
            string? usuario)
        {
            lock (_sync)
            {
                _activo = activo;
                _ultimoCambio = DateTime.Now;
                _usuarioUltimoCambio = string.IsNullOrWhiteSpace(usuario)
                    ? "SISTEMA"
                    : usuario.Trim();

                _ultimoMensaje = activo
                    ? "Envío automático encendido."
                    : "Envío automático apagado. El envío que ya esté en curso terminará antes de detenerse.";
            }

            DespertarWorker();
            return ObtenerEstado();
        }

        public void MarcarInicioCiclo()
        {
            lock (_sync)
            {
                _procesando = true;
                _ultimoInicioCiclo = DateTime.Now;
                _ultimoMensaje = "Procesando integraciones pendientes.";
            }
        }

        public void MarcarFinCiclo(string mensaje)
        {
            lock (_sync)
            {
                _procesando = false;
                _ultimoFinCiclo = DateTime.Now;
                _ultimoMensaje = string.IsNullOrWhiteSpace(mensaje)
                    ? "Ciclo automático terminado."
                    : mensaje;
            }
        }

        public void MarcarError(string mensaje)
        {
            lock (_sync)
            {
                _procesando = false;
                _ultimoFinCiclo = DateTime.Now;
                _ultimoMensaje = string.IsNullOrWhiteSpace(mensaje)
                    ? "Error en el proceso automático."
                    : mensaje;
            }
        }

        public void DespertarWorker()
        {
            try
            {
                _wakeSignal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Ya existe una señal pendiente; no es necesario agregar otra.
            }
        }

        public async Task EsperarSiguienteRevisionAsync(
            TimeSpan intervalo,
            CancellationToken cancellationToken)
        {
            using var linked =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var delayTask = Task.Delay(intervalo, linked.Token);
            var signalTask = _wakeSignal.WaitAsync(linked.Token);

            var completed = await Task.WhenAny(delayTask, signalTask);

            linked.Cancel();

            try
            {
                await completed;
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                // Se canceló únicamente la tarea que perdió la carrera.
            }
        }
    }

    public sealed class IntegracionSapAutomaticoSnapshot
    {
        public bool Activo { get; init; }
        public bool Procesando { get; init; }
        public DateTime? UltimoCambio { get; init; }
        public DateTime? UltimoInicioCiclo { get; init; }
        public DateTime? UltimoFinCiclo { get; init; }
        public string UsuarioUltimoCambio { get; init; } = string.Empty;
        public string UltimoMensaje { get; init; } = string.Empty;
    }
}
