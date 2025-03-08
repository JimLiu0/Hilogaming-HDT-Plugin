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
using System.Text;

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
        public string combatResult { get; set; } // "Win", "Loss", or "Tie"
        public string lethalResult { get; set; } // "NoOneDied", "OpponentDied", or "FriendlyDied"
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
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly string _devUrl = "https://hilo-backend.azurewebsites.net/api/hearthstone-battlegrounds/submit-game-data/";
        private readonly string _prodUrl = "https://hilo-production.azurewebsites.net/api/hearthstone-battlegrounds/submit-game-data/";

        public BattlegroundsGameCollection()
        {
            GameEvents.OnGameStart.Add(OnGameStart);
            GameEvents.OnGameEnd.Add(OnGameEnd);
            GameEvents.OnTurnStart.Add(OnTurnStart);
        }

        public void Dispose()
        {
            _httpClient.Dispose();
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
        }

        private async void OnTurnStart(ActivePlayer player)
        {
            if (Core.Game.CurrentGameMode != GameMode.Battlegrounds)
                return;

            var hero = Core.Game.Player.Hero;
            game.heroPlayed = hero.CardId.Split(new[] { "_SKIN_" }, StringSplitOptions.None)[0];;
            game.heroPlayedName = hero.Card?.LocalizedName ?? "Unknown";

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

            // Log triples for all players
            foreach (var entity in playerEntities)
            {
                var triples = entity.GetTag(GameTag.PLAYER_TRIPLES);
                var playerId = entity.GetTag(GameTag.PLAYER_ID);
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Player {playerId} triples count: {triples}");
            }

            // Shop phase
            if (player == ActivePlayer.Player)
            {
                startOfShopPhaseHealths = currentHealths;
                ProcessFight();

                // Parse combat result from previous turn if we have turns
                if (game.turns != null && game.turns.Length > 0)
                {
                    try
                    {
                        await Task.Delay(5000);
                        ParseCombatResult();
                    }
                    catch (Exception ex)
                    {
                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Error($"Error in ParseCombatResult: {ex}");
                    }
                }

                // Initialize turns array if null
                if (game.turns == null)
                {
                    game.turns = new TurnData[0];
                }

                var nextOpponentId = mainPlayerEntity.GetTag(GameTag.NEXT_OPPONENT_PLAYER_ID);

                // Create new turn and add it to the array
                var turnData = new TurnData
                {
                    turn = currentTurn,
                    numMinionsPlayedThisTurn = 0,
                    numSpellsPlayedThisGame = 0,
                    numResourcesSpentThisGame = 0,
                    tavernTier = 1,
                    opponentId = nextOpponentId
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
                    ProcessTurnMetadata(mainPlayerEntity);

                    try
                    {
                        await Task.Delay(10000);
                        ParseHDTLog();
                    }
                    catch (Exception ex)
                    {
                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Error($"Error in ParseHDTLog: {ex}");
                    }
                }
            }
        }

        private async void OnGameEnd()
        {
            try
            {
                if (Core.Game.CurrentGameMode != GameMode.Battlegrounds)
                    return;

                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("=== Game End ===");

                // Check if game object is initialized
                if (game == null)
                {
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Error("OnGameEnd: game object is null");
                    return;
                }

                // Get player entity with null check
                var playerEntity = Core.Game.Entities.Values.FirstOrDefault(x => x.IsPlayer);
                if (playerEntity == null)
                {
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Error("OnGameEnd: Could not find player entity");
                    return;
                }

                if (game.turns != null && game.turns.Length > 0)
                {
                    ProcessTurnMetadata(playerEntity);
                    await Task.Delay(5000);
                    ParseCombatResult();
                }

                var playerPlaceEntity = Core.Game.Entities.Values
                .FirstOrDefault(e => e.GetTag(GameTag.PLAYER_ID) == playerEntity.GetTag(GameTag.PLAYER_ID) 
                                && e.HasTag(GameTag.PLAYER_LEADERBOARD_PLACE));

                game.placement = playerPlaceEntity?.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE) ?? 
                                playerEntity.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE);
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Final placement: {game.placement}");

                // Calculate final damage and get fight simulation data
                ProcessFight();

                // Update metadata
                game.playerIdentifier = Core.Game.Player.Name;
                game.gameEndDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ff");
                game.gameDurationInSeconds = (int)(DateTime.Now - gameStartTime).TotalSeconds;
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Game duration: {game.gameDurationInSeconds} seconds");

                // Get final board state with null checks
                if (Core.Game.Entities?.Values == null)
                {
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Error("OnGameEnd: Game entities are null");
                }
                else
                {
                    var playerBoardEntities = Core.Game.Entities.Values
                        .Where(e => e != null && e.IsInPlay && e.IsMinion && e.IsControlledBy(playerEntity.GetTag(GameTag.CONTROLLER)))
                        .OrderBy(e => e.GetTag(GameTag.ZONE_POSITION))
                        .ToList();

                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Found {playerBoardEntities.Count} minions on final board");

                    game.finalComp = new FinalComp
                    {
                        turn = Core.Game.GetTurnNumber(),
                        board = playerBoardEntities.Select(x => new BoardMinion
                        {
                            cardID = x.CardId,
                            name = x.Card?.LocalizedName ?? "Unknown",
                            tags = new Tag
                            {
                                ATK = x.GetTag(GameTag.ATK),
                                HEALTH = x.GetTag(GameTag.HEALTH)
                            }
                        }).ToArray()
                    };
                }
                
                await CalculateAndUpdateMmr();
                
                // Add the HTTP POST request after everything else is done
                await SubmitGameData();
            }
            catch (Exception ex)
            {
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Error($"Error in OnGameEnd: {ex.Message}\nStack trace: {ex.StackTrace}");
            }
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

        private void ParseHDTLog()
        {
            try
            {
                // Check if we have any turns to update
                if (game?.turns == null || game.turns.Length == 0)
                {
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("ParseHDTLog: No turns to update");
                    return;
                }

                var hdtLogPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HearthstoneDeckTracker",
                    "Logs",
                    "hdt_log.txt"
                );

                if (!File.Exists(hdtLogPath))
                {
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Error($"HDT log file not found at: {hdtLogPath}");
                    return;
                }

                // Read only the last portion of the file
                string[] lastLines;
                using (var fileStream = new FileStream(hdtLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(fileStream))
                {
                    // Read last 50KB which should be more than enough for recent simulation results
                    var maxBytesToRead = 50000;
                    var buffer = new char[maxBytesToRead];
                    var startPosition = Math.Max(0, fileStream.Length - maxBytesToRead);
                    fileStream.Seek(startPosition, SeekOrigin.Begin);
                    reader.Read(buffer, 0, maxBytesToRead);
                    lastLines = new string(buffer).Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                }

                // Look for the most recent simulation result
                var simulationRegex = new Regex(@"BobsBuddyInvoker\.RunSimulation >> WinRate=(\d+(?:\.\d+)?)% \(Lethal=\d+(?:\.\d+)?%\), TieRate=(\d+(?:\.\d+)?)%, LossRate=(\d+(?:\.\d+)?)% \(Lethal=\d+(?:\.\d+)?%\)");
                var lastTurn = game.turns.Last();

                // Search from most recent to oldest
                for (int i = lastLines.Length - 1; i >= 0; i--)
                {
                    var simMatch = simulationRegex.Match(lastLines[i]);
                    if (simMatch.Success)
                    {
                        var winRate = double.Parse(simMatch.Groups[1].Value);
                        var tieRate = double.Parse(simMatch.Groups[2].Value);
                        var lossRate = double.Parse(simMatch.Groups[3].Value);

                        lastTurn.winOdds = winRate;
                        lastTurn.tieOdds = tieRate;
                        lastTurn.lossOdds = lossRate;

                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Found simulation results - Win: {winRate}%, Tie: {tieRate}%, Loss: {lossRate}%");
                        return;
                    }
                }

                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("No simulation results found in log");
            }
            catch (Exception ex)
            {
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Error($"Error parsing HDT log: {ex}");
            }
        }

        private void ParseCombatResult(bool isEndGame=false)
        {
            try
            {
                // Check if we have any turns to update
                if (game?.turns == null || game.turns.Length == 0)
                {
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("ParseCombatResult: No turns to update");
                    return;
                }

                var hdtLogPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HearthstoneDeckTracker",
                    "Logs",
                    "hdt_log.txt"
                );

                if (!File.Exists(hdtLogPath))
                {
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Error($"HDT log file not found at: {hdtLogPath}");
                    return;
                }

                // Read only the last portion of the file
                string[] lastLines;
                using (var fileStream = new FileStream(hdtLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(fileStream))
                {
                    // Read last 50KB which should be more than enough for recent results
                    var maxBytesToRead = 50000;
                    var buffer = new char[maxBytesToRead];
                    var startPosition = Math.Max(0, fileStream.Length - maxBytesToRead);
                    fileStream.Seek(startPosition, SeekOrigin.Begin);
                    reader.Read(buffer, 0, maxBytesToRead);
                    lastLines = new string(buffer).Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                }

                var combatResultRegex = new Regex(@"BobsBuddyInvoker\.ValidateSimulationResultAsync >> result=(\w+), lethalResult=(\w+)");
                var lastTurn = game.turns.Last();
                
                // Search from most recent to oldest
                for (int i = lastLines.Length - 1; i >= 0; i--)
                {
                    var combatMatch = combatResultRegex.Match(lastLines[i]);
                    if (combatMatch.Success)
                    {
                        // If it's end game we only want to update if the lethal result is not NoOneDied
                        if (!isEndGame || combatMatch.Groups[2].Value != "NoOneDied")
                        {
                            lastTurn.combatResult = combatMatch.Groups[1].Value;
                            lastTurn.lethalResult = combatMatch.Groups[2].Value;
                            return;
                        }
                    }
                }

                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("No combat results found in log");
            }
            catch (Exception ex)
            {
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Error($"Error parsing combat result: {ex}");
            }
        }

        private void ProcessTurnMetadata(Entity mainPlayerEntity)
        {
            var playerEntities = Core.Game.Entities.Values.Where(x => x.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE) != 0).ToList();

            foreach (var entity in playerEntities)
            {
                var isPlayer = entity.IsPlayer;

                if (isPlayer)
                {
                    game.triplesCreated = entity.GetTag(GameTag.PLAYER_TRIPLES);
                }
            }
            var lastTurn = game.turns.Last();
            lastTurn.numMinionsPlayedThisTurn = mainPlayerEntity.GetTag(GameTag.NUM_MINIONS_PLAYED_THIS_TURN);
            lastTurn.numSpellsPlayedThisGame = mainPlayerEntity.GetTag(GameTag.NUM_SPELLS_PLAYED_THIS_GAME);
            lastTurn.numResourcesSpentThisGame = mainPlayerEntity.GetTag(GameTag.NUM_RESOURCES_SPENT_THIS_GAME);
            lastTurn.tavernTier = mainPlayerEntity.GetTag(GameTag.PLAYER_TECH_LEVEL);
        }

        private void ProcessFight()
        {
            try
            {
                // Only process if we have turns
                if (game?.turns == null || game.turns.Length == 0)
                {
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("ProcessFight: No turns to process");
                    return;
                }

                var mainPlayerEntity = Core.Game.Entities.Values.FirstOrDefault(x => x.IsPlayer);
                if (mainPlayerEntity == null)
                {
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Error("ProcessFight: Could not find player entity");
                    return;
                }

                var lastTurn = game.turns.Last();
                if (lastTurn == null)
                {
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Error("ProcessFight: Last turn is null");
                    return;
                }

                // Skip if we don't have an opponent ID yet
                if (lastTurn.opponentId == 0)
                {
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"ProcessFight: No opponent ID for turn {lastTurn.turn}");
                    return;
                }

                // Skip if we don't have combat phase healths
                if (startOfCombatPhaseHealths == null || startOfCombatPhaseHealths.Length == 0)
                {
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("ProcessFight: No combat phase health data");
                    return;
                }

                var playerEntities = Core.Game.Entities.Values.Where(x => x.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE) != 0).ToList();
                var currentHealths = playerEntities.Select(x => new HealthData
                {
                    playerId = x.GetTag(GameTag.PLAYER_ID),
                    health = x.GetTag(GameTag.HEALTH),
                    armor = x.GetTag(GameTag.ARMOR),
                    damage = x.GetTag(GameTag.DAMAGE)
                }).ToArray();

                startOfShopPhaseHealths = currentHealths;

                // Get the healths for opponent and player with null checks
                var opponentCombatHealth = startOfCombatPhaseHealths.FirstOrDefault(x => x.playerId == lastTurn.opponentId);
                var opponentShopHealth = startOfShopPhaseHealths.FirstOrDefault(x => x.playerId == lastTurn.opponentId);
                var playerCombatHealth = startOfCombatPhaseHealths.FirstOrDefault(x => x.playerId == mainPlayerEntity.GetTag(GameTag.PLAYER_ID));
                var playerShopHealth = startOfShopPhaseHealths.FirstOrDefault(x => x.playerId == mainPlayerEntity.GetTag(GameTag.PLAYER_ID));

                if (opponentCombatHealth == null || opponentShopHealth == null || 
                    playerCombatHealth == null || playerShopHealth == null)
                {
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Error("ProcessFight: Missing health data for player or opponent");
                    return;
                }

                // Log health values for debugging
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info(
                    $"Turn {lastTurn.turn} health values:" +
                    $"\nPlayer Combat Health: {playerCombatHealth.totalHealth}" +
                    $"\nPlayer Shop Health: {playerShopHealth.totalHealth}" +
                    $"\nOpponent Combat Health: {opponentCombatHealth.totalHealth}" +
                    $"\nOpponent Shop Health: {opponentShopHealth.totalHealth}");

                // Calculate the damage done, depending on who has different health that's who won
                if (playerShopHealth.totalHealth < playerCombatHealth.totalHealth)
                {
                    lastTurn.heroDamage = -(playerCombatHealth.totalHealth - playerShopHealth.totalHealth);
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"We took {-lastTurn.heroDamage} damage");
                }
                else if (opponentShopHealth.totalHealth < opponentCombatHealth.totalHealth)
                {
                    lastTurn.heroDamage = opponentCombatHealth.totalHealth - opponentShopHealth.totalHealth;
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"We dealt {lastTurn.heroDamage} damage");
                }
                else
                {
                    lastTurn.heroDamage = 0;
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("No damage dealt (tie)");
                }
            }
            catch (Exception ex)
            {
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Error($"Error in ProcessFight: {ex}");
            }
        }

        private async Task SubmitGameData()
        {
            try
            {
                var jsonContent = JsonConvert.SerializeObject(game);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // Choose which URL to use (you can set this based on a config or environment variable)
                var url = _devUrl; // or _prodUrl for production

                var response = await _httpClient.PostAsync(url, content);
                
                if (response.IsSuccessStatusCode)
                {
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Successfully submitted game data to {url}");
                }
                else
                {
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Error($"Failed to submit game data. Status: {response.StatusCode}, Response: {await response.Content.ReadAsStringAsync()}");
                }
            }
            catch (Exception ex)
            {
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Error($"Error submitting game data: {ex.Message}");
            }
        }
    }
}
