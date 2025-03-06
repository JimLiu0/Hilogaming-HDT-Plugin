using Hearthstone_Deck_Tracker.API;
using System;
using Core = Hearthstone_Deck_Tracker.API.Core;
using Hearthstone_Deck_Tracker.Enums;
using Hearthstone_Deck_Tracker.Hearthstone.Entities;
using BattlegroundsGameCollection.Logic;
using Newtonsoft.Json;
using System.IO;
using System.Text.RegularExpressions;
using Hearthstone_Deck_Tracker.Hearthstone;
using HearthDb.Enums;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using Hearthstone_Deck_Tracker.Utility.Battlegrounds;

namespace BattlegroundsGameCollection
{
    public class GameData
    {
        public string playerIdentifier { get; set; }
        public int placement { get; set; }
        public int startingMmr { get; set; }
        public int mmrGained { get; set; }
        public int gameDurationInSeconds { get; set; }
        public string gameEndDate { get; set; }
        public string heroPlayed { get; set; }
        public string heroPlayedName { get; set; }
        public int triplesCreated { get; set; }
        public double battleLuck { get; set; }
        public string server { get; set; }
        public TurnData[] turns { get; set; }
        public FinalComp finalComp { get; set; }
    }

    public class TurnData
    {
        public int turn { get; set; }
        public int opponentId { get; set; }
        public int heroDamage { get; set; } // Positive if we dealt damage, negative if we took damage, 0 for tie
        public double winOdds { get; set; }
        public double tieOdds { get; set; }
        public double lossOdds { get; set; }
        public double averageDamageTaken { get; set; }
        public double averageDamageDealt { get; set; }
        public string actualCombatResult { get; set; } // "Win", "Loss", or "Tie"
        public string actualLethalResult { get; set; } // "NoOneDied", "OpponentDied", or "FriendlyDied"
        public int numMinionsPlayedThisTurn { get; set; }
        public int numSpellsPlayedThisGame { get; set; }
        public int numResourcesSpentThisGame { get; set; }
        public int tavernTier { get; set; }
    }

    public class FinalComp
    {
        public BoardMinion[] board { get; set; }
        public int turn { get; set; }
    }

    public class BoardMinion
    {
        public string cardID { get; set; }
        public string name { get; set; }
        public Tag tags { get; set; }
    }

    public class Tag
    {
        public int ATK { get; set; }
        public int HEALTH { get; set; }
    }

    // Needed to calculate damage
    public class HealthData
    {
        public int playerId { get; set; }
        public int health { get; set; }
        public int armor { get; set; }
        public int damage { get; set; }
        public int totalHealth => health + armor - damage;
    }

    public class BattlegroundsGameCollection : IDisposable
    {
        private GameData game = new GameData();
        private DateTime gameStartTime;
        private HealthData[] startOfShopPhaseHealths = new HealthData[8];
        private HealthData[] startOfCombatPhaseHealths = new HealthData[8];

        public BattlegroundsGameCollection()
        {
            GameEvents.OnGameStart.Add(OnGameStart);
            GameEvents.OnGameEnd.Add(OnGameEnd);
            GameEvents.OnTurnStart.Add(OnTurnStart);
        }

        public void Dispose()
        {
            game = null;
        }

        private void OnGameStart()
        {
            if (Core.Game.CurrentGameMode != GameMode.Battlegrounds) return;

            gameStartTime = DateTime.Now;
            game = new GameData
            {
                turns = new TurnData[0],
                finalComp = new FinalComp
                {
                    board = new BoardMinion[0],
                    turn = 0
                }
            };
            startOfShopPhaseHealths = new HealthData[8];
            startOfCombatPhaseHealths = new HealthData[8];
            switch (Core.Game.CurrentRegion)
            {
                case Region.US:
                    game.server = "REGION_US";
                    break;
                case Region.EU:
                    game.server = "REGION_EU";
                    break;
                default:
                    game.server = "REGION_AP";
                    break;
            }
            // Figure out playerIdentifier
        }

        private void OnTurnStart(ActivePlayer player)
        {
            if (Core.Game.CurrentGameMode != GameMode.Battlegrounds)
                return;

            var currentTurn = Core.Game.GetTurnNumber();
            var playerEntities = Core.Game.Entities.Values.Where(x => x.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE) != 0).ToList();
            var mainPlayerEntity = Core.Game.Entities.Values.FirstOrDefault(x => x.IsPlayer);

            var currentHealths = playerEntities.Select(x => new HealthData
            {
                playerId = x.GetTag(GameTag.PLAYER_ID),
                health = x.GetTag(GameTag.HEALTH),
                armor = x.GetTag(GameTag.ARMOR),
                damage = x.GetTag(GameTag.DAMAGE)
            }).ToArray();

            // Shop phase
            if (player == ActivePlayer.Player)
            {
                ProcessFight();

                // Initialize turns array if null
                if (game.turns == null)
                {
                    game.turns = new TurnData[0];
                }

                // Create new turn and add it to the array
                var turnData = new TurnData
                {
                    turn = currentTurn,
                    numMinionsPlayedThisTurn = 0,
                    numSpellsPlayedThisGame = 0,
                    numResourcesSpentThisGame = 0,
                    tavernTier = 1
                };

                // Add the new turn to the array
                var newTurns = new TurnData[game.turns.Length + 1];
                Array.Copy(game.turns, newTurns, game.turns.Length);
                newTurns[game.turns.Length] = turnData;
                game.turns = newTurns;
            }

            // Combat phase
            if (player == ActivePlayer.Opponent)
            {
                startOfCombatPhaseHealths = currentHealths;

                if (game.turns != null && game.turns.Length > 0)
                {
                    var lastTurn = game.turns.Last();

                    game.triplesCreated = mainPlayerEntity.GetTag(GameTag.PLAYER_TRIPLES);
                    lastTurn.numMinionsPlayedThisTurn = mainPlayerEntity.GetTag(GameTag.NUM_MINIONS_PLAYED_THIS_TURN);
                    lastTurn.numSpellsPlayedThisGame = mainPlayerEntity.GetTag(GameTag.NUM_SPELLS_PLAYED_THIS_GAME);
                    lastTurn.numResourcesSpentThisGame = mainPlayerEntity.GetTag(GameTag.NUM_RESOURCES_SPENT_THIS_GAME);
                    lastTurn.tavernTier = mainPlayerEntity.GetTag(GameTag.PLAYER_TECH_LEVEL);
                }
            }
        }

        private void Log()
        {
            var jsonContent = JsonConvert.SerializeObject(game, Formatting.Indented);

            // Create BGGames directory if it doesn't exist
            var bgGamesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BGGames");
            if (!Directory.Exists(bgGamesDir))
            {
                Directory.CreateDirectory(bgGamesDir);
            }

            // Create HSReplay subdirectory
            var hsReplayDir = Path.Combine(bgGamesDir, "HSReplay");
            if (!Directory.Exists(hsReplayDir))
            {
                Directory.CreateDirectory(hsReplayDir);
            }

            // Generate filename based on current date/time
            var filename = $"game_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json";
            var fullPath = Path.Combine(hsReplayDir, filename);

            // Save the JSON file
            File.WriteAllText(fullPath, jsonContent);
        }

        private async void OnGameEnd()
        {
            if (Core.Game.CurrentGameMode != GameMode.Battlegrounds)
                return;

            // Calculate final damage and get fight simulation data TBD
            ProcessFight();
      
            // Do placement stuff and meta data stuff
            game.placement = Core.Game.Entities.Values.FirstOrDefault(x => x.IsPlayer).GetTag(GameTag.PLAYER_LEADERBOARD_PLACE);
            game.gameEndDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ff");
            game.gameDurationInSeconds = (int)(DateTime.Now - gameStartTime).TotalSeconds;
            game.heroPlayed = Core.Game.Player.Hero.CardId;
            game.heroPlayedName = Core.Game.Player.Hero.Card?.LocalizedName;

            var playerEntity = Core.Game.Entities.Values.FirstOrDefault(x => x.IsPlayer);
            var playerBoardEntities = Core.Game.Entities.Values
                .Where(e => e.IsInPlay && e.IsMinion && e.IsControlledBy(playerEntity.GetTag(GameTag.CONTROLLER)))
                .OrderBy(e => e.GetTag(GameTag.ZONE_POSITION));

            game.finalComp = new FinalComp();
            game.finalComp.turn = Core.Game.GetTurnNumber();
            game.finalComp.board = playerBoardEntities.Select(x => new BoardMinion
            {
                cardID = x.CardId,
                name = x.Card?.LocalizedName ?? "Unknown",
                tags = new Tag
                {
                    ATK = x.GetTag(GameTag.ATK),
                    HEALTH = x.GetTag(GameTag.HEALTH)
                }
            }).ToArray();

            // Do final board stuff phase 2
            await CalculateAndUpdateMmr();
        }

        private async Task CalculateAndUpdateMmr()
        {
            // Wait 5 seconds to ensure MMR has been updated
            await Task.Delay(5000);

            var instance = BattlegroundsLastGames.Instance;
            var latestGame = instance.Games.Last();
            int ratingAfter = latestGame.RatingAfter;
            int ratingBefore = latestGame.Rating;
            int mmrChange = ratingAfter - ratingBefore;

            game.startingMmr = ratingBefore;
            game.mmrGained = mmrChange;

            Log();
        }

        private void ProcessFight()
        {
            // Only process if we have turns
            if (game.turns == null || game.turns.Length == 0)
            {
                return;
            }

            var playerEntities = Core.Game.Entities.Values.Where(x => x.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE) != 0).ToList();
            var mainPlayerEntity = Core.Game.Entities.Values.FirstOrDefault(x => x.IsPlayer);
            var currentHealths = playerEntities.Select(x => new HealthData
            {
                playerId = x.GetTag(GameTag.PLAYER_ID),
                health = x.GetTag(GameTag.HEALTH),
                armor = x.GetTag(GameTag.ARMOR),
                damage = x.GetTag(GameTag.DAMAGE)
            }).ToArray();

            startOfShopPhaseHealths = currentHealths;

            var lastTurn = game.turns.Last();
            // Figure out how much damage was done during the last combat phase

            // Get the opponent id from last turn
            var opponentId = lastTurn.opponentId;

            // Get the healths for opponent and player
            var opponentHealthStartOfCombat = startOfCombatPhaseHealths.FirstOrDefault(x => x.playerId == opponentId).totalHealth;
            var opponentHealthStartOfShop = startOfShopPhaseHealths.FirstOrDefault(x => x.playerId == opponentId).totalHealth;
            var playerHealthStartOfCombat = startOfCombatPhaseHealths.FirstOrDefault(x => x.playerId == mainPlayerEntity.GetTag(GameTag.PLAYER_ID)).totalHealth;
            var playerHealthStartOfShop = startOfShopPhaseHealths.FirstOrDefault(x => x.playerId == mainPlayerEntity.GetTag(GameTag.PLAYER_ID)).totalHealth;

            // Calculate the damage done, depending on who has different health that's who won
            if (playerHealthStartOfShop < playerHealthStartOfCombat)
            {
                lastTurn.heroDamage = playerHealthStartOfCombat - playerHealthStartOfShop;
            }
            else if (opponentHealthStartOfShop < opponentHealthStartOfCombat)
            {
                lastTurn.heroDamage = opponentHealthStartOfCombat - opponentHealthStartOfShop;
            }
            else
            {
                lastTurn.heroDamage = 0;
            }
            // Get the simulation results from the fight phase and also add to the last turn can be done in 2nd phase
        }
    }
}
