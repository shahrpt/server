using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Server.Models;

namespace Server.Pages
{
    [UsedImplicitly]
    public class PatreonControlPanelModel : PageModel
    {
        private readonly AppDbContext _context;

        public PatreonControlPanelModel(AppDbContext context)
        {
            _context = context;
        }

        public IActionResult OnGet()
        {
            return Page();
        }

        [UsedImplicitly]
        [BindProperty]
        public SetPatreonLevelRequest Req { get; set; }

        public IActionResult OnPost()
        {
            if (!ModelState.IsValid) return Page();

            var player = _context.Players.FirstOrDefault(p => p.SteamId == Req.SteamId);
            if (player == null)
            {
                player = new Player() { SteamId = Req.SteamId };
                _context.Add(player);
            }
            player.PatreonLevel = Req.PatreonLevel;
            player.Comment = Req.Comment;
            _context.SaveChanges();
            return Redirect("PatreonControlPanel");
        }

        public IEnumerable<PatreonPlayer> GetAllPatreons()
        {
            return _context.Players.Where(p => p.PatreonLevel > 0)
                .Select(p => new PatreonPlayer() {SteamId = p.SteamId, PatreonLevel = p.PatreonLevel, Comment = p.Comment})
                .ToArray();
        }
    }

    public class PatreonPlayer
    {
        public ulong SteamId { get; set; }
        public ushort PatreonLevel { get; set; }
        public string Comment { get; set; }
    }

    [UsedImplicitly]
    public class SetPatreonLevelRequest
    {
        [DisplayName("Steam ID")]
        public ulong SteamId { get; set; }
        [DisplayName("Patreon Level")]
        public ushort PatreonLevel { get; set; }
        [DisplayName("Comment")]
        public string Comment { get; set; }
    }
}