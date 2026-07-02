using System;

namespace Plataforma_CG.Models.Chat
{
    public class NuevaConversacionDto
    {
        public int IdArea { get; set; }
    }

    public class EnviarMensajeDto
    {
        public int IdConversacion { get; set; }
        public string Texto { get; set; } = string.Empty;
    }

    public class MensajeViewModel
    {
        public int IdMensaje { get; set; }
        public string AutorUsuarioId { get; set; }
        public string Texto { get; set; }
        public DateTime Fecha { get; set; }
        public bool EsMio { get; set; }
    }

    public class ConversacionResumenViewModel
    {
        public int IdConversacion { get; set; }
        public string UsuarioId { get; set; }
        public string NombreUsuario { get; set; }
        public int IdArea { get; set; }
        public string Area { get; set; }
        public string ResponsableUsuarioId { get; set; }
        public string NombreResponsable { get; set; }
        public DateTime FechaInicio { get; set; }
        public bool Cerrada { get; set; }
        public DateTime? UltimoMensajeFecha { get; set; }

        public int UnreadCount { get; set; }
    }
}
