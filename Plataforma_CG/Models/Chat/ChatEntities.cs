using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Plataforma_CG.Models.Chat
{
    // ====== ÁREAS ======
    [Table("ChatAreas")]
    public class ChatArea
    {
        [Key]
        public int IdArea { get; set; }

        [Required, StringLength(80)]
        public string Nombre { get; set; } = string.Empty;

        // correo / login del responsable (UsuariosAD.UsuarioAD o UsuariosSQL.Usuario)
        [StringLength(200)]
        public string? ResponsableUsuarioId { get; set; }

        public bool Activo { get; set; } = true;

        public ICollection<ChatConversacion> Conversaciones { get; set; }
            = new List<ChatConversacion>();
    }

    // ====== CONVERSACIONES ======
    [Table("ChatConversaciones")]
    public class ChatConversacion
    {
        [Key]
        public int IdConversacion { get; set; }

        // Usuario que inició el chat (correo / login)
        [Required, StringLength(200)]
        public string UsuarioId { get; set; } = string.Empty;

        // FK al área
        public int IdArea { get; set; }

        [ForeignKey(nameof(IdArea))]
        public ChatArea Area { get; set; }

        public DateTime FechaInicio { get; set; } = DateTime.Now;

        public bool Cerrada { get; set; } = false;

        public ICollection<ChatMensaje> Mensajes { get; set; }
            = new List<ChatMensaje>();
    }

    // ====== MENSAJES ======
    [Table("ChatMensajes")]
    public class ChatMensaje
    {
        [Key]
        public int IdMensaje { get; set; }

        public int IdConversacion { get; set; }

        [ForeignKey(nameof(IdConversacion))]
        public ChatConversacion Conversacion { get; set; }

        // Usuario que envía el mensaje (cliente o responsable)
        [Required, StringLength(200)]
        public string AutorUsuarioId { get; set; } = string.Empty;

        [Required]
        public string Texto { get; set; } = string.Empty;

        public DateTime Fecha { get; set; } = DateTime.Now;

        public bool Leido { get; set; } = false;
    }
}
