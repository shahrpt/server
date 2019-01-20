using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Server.Models;

namespace Server.Controllers
{
    // TODO: Remove it
    [Route("api/vscripts")]
    [Route("api/[controller]")]
    [ApiController]
    public class OverthrowController : ControllerBase
    {
        private readonly AppDbContext _context;

        public OverthrowController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        [Route("auto-pick")]
        public ActionResult<AutoPickResponse> AutoPick(AutoPickRequest request)
        {
            var realSteamIds = request.Players.Select(ulong.Parse).ToList();
            var selectedHeroes = request.SelectedHeroes;
            var players = _context.Players.Where(p => realSteamIds.Contains(p.SteamId))
                .Select(p => new
                {
                    SteamId = p.SteamId.ToString(),
                    HeroesMap = p.Matches
                        .Where(m => m.Match.MapName == request.MapName)
                        .OrderByDescending(m => m.MatchId)
                        .Take(100)
                        .GroupBy(m => m.Hero)
                        .OrderByDescending(g => g.Count())
                        .Take(10)
                        .Select(g => g.Key)
                        .ToList(),
                    HeroesGlobal = p.Matches.OrderByDescending(m => m.MatchId)
                        .Take(100)
                        .GroupBy(m => m.Hero)
                        .OrderByDescending(g => g.Count())
                        .Take(10)
                        .Select(g => g.Key)
                        .ToList(),
                })
                .ToList();

            return new AutoPickResponse()
            {
                Players = players.Select(p => new AutoPickResponse.Player()
                {
                    SteamId = p.SteamId.ToString(),
                    Heroes = (p.HeroesMap.Except(selectedHeroes).Count() >= 3 ? p.HeroesMap : p.HeroesGlobal)
                        .Except(selectedHeroes)
                        .Take(3)
                        .ToList(),
                })
            };
        }

        [HttpPost]
        [Route("before-match")]
        public BeforeMatchResponse BeforeMatch(BeforeMatchRequest request)
        {
            var realSteamIds = request.Players.Select(ulong.Parse).ToList();
            var responses = _context.Players.Where(p => realSteamIds.Contains(p.SteamId))
                .Select(p => new
                {
                    SteamId = p.SteamId.ToString(),
                    Patreon =
                        new BeforeMatchResponse.Patreon()
                        {
                            Level = p.PatreonLevel,
                            EmblemEnabled = p.PatreonEmblemEnabled.GetValueOrDefault(true),
                            EmblemColor = p.PatreonEmblemColor ?? "White",
                            BootsEnabled = p.PatreonBootsEnabled.GetValueOrDefault(true),
                        },
                    MatchesOnMap =
                        p.Matches.Where(m => m.Match.MapName == request.MapName)
                            .OrderByDescending(m => m.MatchId)
                            .Select(m => new {IsWinner = m.Team == m.Match.Winner, m.Kills, m.Deaths, m.Assists})
                            .ToList(),
                    SmartRandomHeroesMap =
                        p.Matches.Where(m => m.Match.MapName == request.MapName)
                            .Where(m => m.PickReason == "pick")
                            .OrderByDescending(m => m.MatchId)
                            .Take(100)
                            .GroupBy(m => m.Hero)
                            .Where(g => g.Count() >= (int) Math.Ceiling(p.Matches.Count() / 20.0))
                            .Select(g => g.Key)
                            .ToList(),
                    SmartRandomHeroesGlobal = p.Matches
                        .Where(m => m.PickReason == "pick")
                        .OrderByDescending(m => m.MatchId)
                        .Take(100)
                        .GroupBy(m => m.Hero)
                        .Where(g => g.Count() >= (int) Math.Ceiling(p.Matches.Count() / 20.0))
                        .Select(g => g.Key)
                        .ToList(),
                    LastSmartRandomUse = p.Matches.Where(m => m.PickReason == "smart-random")
                        .OrderByDescending(m => m.Match.EndedAt)
                        .Take(1)
                        .Select(m => m.Match.EndedAt)
                        .FirstOrDefault()
                })
                .ToList();

            return new BeforeMatchResponse()
            {
                Players = request.Players.Select(id =>
                    {
                        var response = responses.FirstOrDefault(p => p.SteamId == id);
                        if (response == null)
                        {
                            return new BeforeMatchResponse.Player()
                            {
                                SteamId = id.ToString(),
                                Patreon = new BeforeMatchResponse.Patreon()
                                {
                                    Level = 0, EmblemEnabled = true, EmblemColor = "White", BootsEnabled = true,
                                },
                                SmartRandomHeroesError = "no_stats",
                            };
                        }

                        var player = new BeforeMatchResponse.Player
                        {
                            SteamId = id.ToString(),
                            // TODO: Remove it
                            PatreonLevel = response.Patreon.Level,
                            Patreon = response.Patreon,
                            Streak = response.MatchesOnMap.TakeWhile(w => w.IsWinner).Count(),
                            BestStreak = response.MatchesOnMap.LongestStreak(w => w.IsWinner),
                            AverageKills = response.MatchesOnMap.Select(x => (double) x.Kills)
                                .DefaultIfEmpty()
                                .Average(),
                            AverageDeaths = response.MatchesOnMap.Select(x => (double) x.Deaths)
                                .DefaultIfEmpty()
                                .Average(),
                            AverageAssists =
                                response.MatchesOnMap.Select(x => (double) x.Assists).DefaultIfEmpty().Average(),
                            Wins = response.MatchesOnMap.Count(w => w.IsWinner),
                            Loses = response.MatchesOnMap.Count(w => !w.IsWinner),
                        };

                        var canUseSmartRandom = response.Patreon.Level >= 1 ||
                                                (DateTime.UtcNow - response.LastSmartRandomUse).TotalDays >= 1;
                        if (canUseSmartRandom)
                        {
                            var heroes = response.SmartRandomHeroesMap.Count >= 5
                                ? response.SmartRandomHeroesMap
                                : response.SmartRandomHeroesGlobal;

                            if (heroes.Count >= 3)
                            {
                                player.SmartRandomHeroes = heroes;
                            }
                            else
                            {
                                player.SmartRandomHeroesError = "no_stats";
                            }
                        }
                        else
                        {
                            player.SmartRandomHeroesError = "cooldown";
                        }

                        return player;
                    })
                    .ToList()
            };
        }

        [HttpPost]
        [Route("end-match")]
        public ActionResult EndMatch([FromBody] EndMatchRequest request)
        {
            var requestedSteamIds = request.Players.Select(p => ulong.Parse(p.SteamId)).ToList();
            var existingPlayers = _context.Players.Where(p => requestedSteamIds.Contains(p.SteamId)).ToList();

            var newPlayers = request.Players.Where(r => existingPlayers.All(p => p.SteamId.ToString() != r.SteamId))
                .Select(p => new Player() {SteamId = ulong.Parse(p.SteamId)})
                .ToList();

            foreach (var player in request.Players.Where(p => p.PatreonUpdate != null))
            {
                var existingPlayer = existingPlayers.FirstOrDefault(p => p.SteamId.ToString() == player.SteamId) ??
                                     newPlayers.FirstOrDefault(p => p.SteamId.ToString() == player.SteamId);
                if (existingPlayer == null) continue;
                existingPlayer.PatreonBootsEnabled = player.PatreonUpdate.BootsEnabled;
                existingPlayer.PatreonEmblemEnabled = player.PatreonUpdate.EmblemEnabled;
                existingPlayer.PatreonEmblemColor = player.PatreonUpdate.EmblemColor;
            }

            var match = new Match
            {
                MatchId = request.MatchId,
                MapName = request.MapName,
                Winner = request.Winner,
                Duration = request.Duration,
                EndedAt = DateTime.UtcNow
            };

            match.Players = request.Players.Select(p => new MatchPlayer
                {
                    Match = match,
                    SteamId = ulong.Parse(p.SteamId),
                    PlayerId = p.PlayerId,
                    Team = p.Team,
                    Hero = p.Hero,
                    PickReason = p.PickReason,
                    Kills = p.Kills,
                    Deaths = p.Deaths,
                    Assists = p.Assists,
                    Level = p.Level,
                    Items = p.Items,
                })
                .ToList();

            _context.AddRange(newPlayers);
            _context.Matches.Add(match);
            _context.SaveChanges();

            return Ok();
        }
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public class EndMatchRequest
    {
        [Required] public uint MatchId { get; set; }
        [Required] public string MapName { get; set; }
        [Required] public ushort Winner { get; set; }
        [Required] public uint Duration { get; set; }

        [Required] public IEnumerable<Player> Players { get; set; }

        [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
        public class Player
        {
            [Required] public ushort PlayerId { get; set; }
            [Required] public string SteamId { get; set; }
            [Required] public ushort Team { get; set; }
            [Required] public string Hero { get; set; }
            [Required] public string PickReason { get; set; }
            [Required] public uint Kills { get; set; }
            [Required] public uint Deaths { get; set; }
            [Required] public uint Assists { get; set; }
            [Required] public uint Level { get; set; }
            [Required] public List<MatchPlayerItem> Items { get; set; }
            public PatreonUpdate PatreonUpdate { get; set; }
        }

        [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
        public class PatreonUpdate
        {
            public bool EmblemEnabled { get; set; }
            public string EmblemColor { get; set; }
            public bool BootsEnabled { get; set; }
        }
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public class AutoPickRequest
    {
        public string MapName { get; set; }
        public List<string> SelectedHeroes { get; set; }
        public List<string> Players { get; set; }
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public class AutoPickResponse
    {
        public IEnumerable<Player> Players { get; set; }

        [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
        public class Player
        {
            public string SteamId { get; set; }
            public List<string> Heroes { get; set; }
        }
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public class BeforeMatchRequest
    {
        public string MapName { get; set; }
        public List<string> Players { get; set; }
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public class BeforeMatchResponse
    {
        public IEnumerable<Player> Players { get; set; }

        [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
        public class Player
        {
            public string SteamId { get; set; }
            public List<string> SmartRandomHeroes { get; set; }
            public string SmartRandomHeroesError { get; set; }
            public int Streak { get; set; }

            public int BestStreak { get; set; }

            // TODO: Remove it
            public ushort PatreonLevel { get; set; }
            public Patreon Patreon { get; set; }
            public double AverageKills { get; set; }
            public double AverageDeaths { get; set; }
            public double AverageAssists { get; set; }
            public int Wins { get; set; }
            public int Loses { get; set; }
        }

        [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
        public class Patreon
        {
            public ushort Level { get; set; }
            public bool EmblemEnabled { get; set; }
            public string EmblemColor { get; set; }
            public bool BootsEnabled { get; set; }
        }
    }
}