using Hearthstone_Deck_Tracker.API;
using System;
using System.Windows.Media;
using Core = Hearthstone_Deck_Tracker.API.Core;
using Hearthstone_Deck_Tracker.Enums;
using Hearthstone_Deck_Tracker.Hearthstone.Entities;
using BattlegroundsGameCollection.Logic;
using Newtonsoft.Json;
using System.IO;
using System.Text.RegularExpressions;
using HearthMirror;
using Hearthstone_Deck_Tracker.Hearthstone;
using Hearthstone_Deck_Tracker.Stats;
using HearthDb.Enums;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BattlegroundsGameCollection
{
    public class PlayerHealthInfo
    {
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public string HeroCardId { get; set; }
        public string HeroName { get; set; }
        public int Health { get; set; }
        public int CurrentArmor { get; set; }
        public int InitialArmor { get; set; }
        public int Damage { get; set; }
        public int TotalHealth => Health - Damage + CurrentArmor;
    }

    public class TurnData
    {
        public int Turn { get; set; }
        public double WinRate { get; set; }
        public double TieRate { get; set; }
        public double LossRate { get; set; }
        public double TheirDeathRate { get; set; }
        public double MyDeathRate { get; set; }
        public string ActualCombatResult { get; set; }  // "Win", "Loss", or "Tie"
        public string ActualLethalResult { get; set; }  // "NoOneDied", "OpponentDied", or "FriendlyDied"
        public int NumMinionsPlayedThisTurn { get; set; }
        public int NumSpellsPlayedThisGame { get; set; }
        public int NumResourcesSpentThisGame { get; set; }
        [JsonIgnore]
        public List<PlayerHealthInfo> PlayerHealths { get; set; } = new List<PlayerHealthInfo>();
        public int HeroDamage { get; set; } // Positive if we dealt damage, negative if we took damage, 0 for tie
        public string OpponentId { get; set; } // Store opponent's ID for this turn
    }

    public class BattlegroundsGameData
    {
        public string PlayerIdentifier { get; set; }
        public int Placement { get; set; }
        public int StartingMmr { get; set; }
        public int FinalMmr { get; set; }
        public int MmrGained { get; set; }
        public int GameDurationInSeconds { get; set; }
        public string GameEndDate { get; set; }
        public string HeroPlayed { get; set; }
        public string HeroPlayedName { get; set; }
        public string AnomalyId { get; set; }
        public string AnomalyName { get; set; }
        public int TriplesCreated { get; set; }
        public string Region { get; set; }
        public List<BoardMinion> FinalBoard { get; set; } = new List<BoardMinion>();
        public List<TurnData> Turns { get; set; } = new List<TurnData>();
    }

    public class BoardMinion
    {
        public string CardId { get; set; }
        public string Name { get; set; }
        public int Attack { get; set; }
        public int Health { get; set; }
        public bool IsTaunt { get; set; }
        public bool IsDivineShield { get; set; }
        public bool IsReborn { get; set; }
        public bool IsPoisonous { get; set; }
        public bool isVenomous { get; set; }
        public List<string> Enchantments { get; set; } = new List<string>();
    }

    public class BattlegroundsGameCollection : IDisposable
    {
        private DateTime gameStartTime;
        private int _totalDamageDealt;
        private int _triplesCreated;
        private int _startingMmr;
        private string _currentHeroId;
        private string _currentHeroName;
        private string _currentAnomalyId;
        private string _currentAnomalyName;
        private int _lastCombatDamageDealt;
        private bool _isInCombat;
        private Entity _lastCombatOpponent;
        private List<TurnData> _turns = new List<TurnData>();
        private static readonly Regex SimulationResultRegex = new Regex(@"WinRate=(\d+(?:\.\d+)?)% \(Lethal=(\d+(?:\.\d+)?)%\), TieRate=(\d+(?:\.\d+)?)%, LossRate=(\d+(?:\.\d+)?)% \(Lethal=(\d+(?:\.\d+)?)%\)");
        private static readonly Regex OpponentSnapshotRegex = new Regex(@"BattlegroundsBoardState\.SnapshotCurrentBoard >> Snapshotting board state for (.*?) with player id (\d+)");
        private static readonly Regex CombatValidationRegex = new Regex(@"BobsBuddyInvoker\.ValidateSimulationResultAsync >> result=(\w+), lethalResult=(\w+)");
        private string _currentOpponentName;
        private string _currentOpponentId;

        private readonly InputMoveManager inputMoveManager;

        public BattlegroundsGameCollection()
        {
            inputMoveManager = new InputMoveManager();
            GameEvents.OnGameStart.Add(OnGameStart);
            GameEvents.OnGameEnd.Add(OnGameEnd);
            GameEvents.OnTurnStart.Add(OnTurnStart);
            GameEvents.OnPlayerCreateInPlay.Add(OnPlayerCreateInPlay);
            GameEvents.OnEntityWillTakeDamage.Add(OnEntityWillTakeDamage);
        }

        private void OnTurnStart(ActivePlayer player)
        {
            if (Core.Game.CurrentGameMode != GameMode.Battlegrounds)
                return;

            // Log current game state
            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"OnTurnStart - Turn {Core.Game.GetTurnNumber()}, Player: {player}");
            
            // Update hero information from Core.Game.Player.Hero
            var hero = Core.Game.Player.Hero;
            if (hero != null)
            {
                _currentHeroId = hero.CardId;
                _currentHeroName = hero.Card?.LocalizedName;
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"OnTurnStart - Hero: {_currentHeroName} ({_currentHeroId})");
            }

            // Track turn statistics
            var playerEntity = Core.Game.Entities.Values.FirstOrDefault(x => x.IsPlayer);
            var currentTurn = Core.Game.GetTurnNumber();

            // Get all player entities by leaderboard place
            var playerEntities = Core.Game.Entities.Values.Where(x => x.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE) != 0).ToList();

            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Found {playerEntities.Count} player entities with leaderboard places");

            // Create or get current turn data
            var turnData = _turns.FirstOrDefault(t => t.Turn == currentTurn);
            if (turnData == null)
            {
                turnData = new TurnData { 
                    Turn = currentTurn,
                    NumMinionsPlayedThisTurn = playerEntity?.GetTag(GameTag.NUM_MINIONS_PLAYED_THIS_TURN) ?? 0,
                    NumSpellsPlayedThisGame = playerEntity?.GetTag(GameTag.NUM_SPELLS_PLAYED_THIS_GAME) ?? 0,
                    NumResourcesSpentThisGame = playerEntity?.GetTag(GameTag.NUM_RESOURCES_SPENT_THIS_GAME) ?? 0
                };
                _turns.Add(turnData);
            }

            // Set opponent ID from the next opponent player ID tag
            if (playerEntity != null)
            {
                var nextOpponentId = playerEntity.GetTag(GameTag.NEXT_OPPONENT_PLAYER_ID);
                if (nextOpponentId > 0)
                {
                    turnData.OpponentId = nextOpponentId.ToString();
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Set opponent ID for turn {currentTurn} to {nextOpponentId}");

                    // Calculate hero damage based on health changes if we have previous turn data
                    if (currentTurn > 1)
                    {
                        var previousTurn = _turns.FirstOrDefault(t => t.Turn == currentTurn - 1);
                        if (previousTurn != null)
                        {
                            var ourPreviousHealth = previousTurn.PlayerHealths
                                .FirstOrDefault(p => p.PlayerId == Core.Game.Player.Id.ToString());
                            var ourCurrentHealth = turnData.PlayerHealths
                                .FirstOrDefault(p => p.PlayerId == Core.Game.Player.Id.ToString());
                            var theirPreviousHealth = previousTurn.PlayerHealths
                                .FirstOrDefault(p => p.PlayerId == turnData.OpponentId);
                            var theirCurrentHealth = turnData.PlayerHealths
                                .FirstOrDefault(p => p.PlayerId == turnData.OpponentId);

                            // Log health values for debugging
                            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info(
                                $"Turn {currentTurn} health values:" +
                                $"\nOur Previous Health: {ourPreviousHealth?.TotalHealth}" +
                                $"\nOur Current Health: {ourCurrentHealth?.TotalHealth}" +
                                $"\nTheir Previous Health: {theirPreviousHealth?.TotalHealth}" +
                                $"\nTheir Current Health: {theirCurrentHealth?.TotalHealth}");

                            // Calculate hero damage based on health differences
                            if (ourPreviousHealth != null && ourCurrentHealth != null && 
                                theirPreviousHealth != null && theirCurrentHealth != null)
                            {
                                var ourHealthDiff = ourPreviousHealth.TotalHealth - ourCurrentHealth.TotalHealth;
                                var theirHealthDiff = theirPreviousHealth.TotalHealth - theirCurrentHealth.TotalHealth;

                                if (theirHealthDiff > 0)
                                {
                                    // We dealt damage
                                    turnData.HeroDamage = theirHealthDiff;
                                }
                                else if (ourHealthDiff > 0)
                                {
                                    // We took damage
                                    turnData.HeroDamage = -ourHealthDiff;
                                }
                                else
                                {
                                    // No health changes - likely a tie
                                    turnData.HeroDamage = 0;
                                }

                                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info(
                                    $"Turn {currentTurn} hero damage calculation:" +
                                    $"\nOur Health Diff: {ourHealthDiff}" +
                                    $"\nTheir Health Diff: {theirHealthDiff}" +
                                    $"\nFinal Hero Damage: {turnData.HeroDamage}");
                            }
                        }
                    }
                }
            }

            // Process player health info
            for (int i = 0; i < playerEntities.Count; i++)
            {
                var pey = playerEntities[i];
                var playerId = pey.GetTag(GameTag.PLAYER_ID);
                var playerName = Core.Game.Entities.Values
                    .FirstOrDefault(e => e.HasTag(GameTag.PLAYER_ID) && e.GetTag(GameTag.PLAYER_ID) == playerId && e.HasTag(GameTag.PLAYSTATE))
                    ?.GetTag(GameTag.PLAYER_ID)
                    .ToString() ?? "Unknown";

                // Get the hero entity for this player
                var heroEntity = Core.Game.Entities.Values
                    .FirstOrDefault(e => e.IsHero && e.GetTag(GameTag.PLAYER_ID) == playerId);

                if (heroEntity != null)
                {
                    var healthInfo = new PlayerHealthInfo
                    {
                        PlayerId = playerId.ToString(),
                        PlayerName = playerName,
                        HeroCardId = heroEntity.CardId,
                        HeroName = heroEntity.Card?.LocalizedName ?? "Unknown",
                        Health = heroEntity.GetTag(GameTag.HEALTH),
                        CurrentArmor = heroEntity.GetTag(GameTag.ARMOR),
                        InitialArmor = currentTurn == 1 ? heroEntity.GetTag(GameTag.ARMOR) : turnData.PlayerHealths.Find(p => p.PlayerId == playerId.ToString())?.InitialArmor ?? 0,
                        Damage = heroEntity.GetTag(GameTag.DAMAGE)
                    };

                    turnData.PlayerHealths.Add(healthInfo);

                    // Log each player's health info with detailed damage info
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info(
                        $"Player Health Info - Turn {currentTurn}: " +
                        $"Player={healthInfo.PlayerName} ({healthInfo.PlayerId}), " +
                        $"Hero={healthInfo.HeroName} ({healthInfo.HeroCardId}), " +
                        $"Base Health={healthInfo.Health}, " +
                        $"Current Armor={healthInfo.CurrentArmor}, " +
                        $"Initial Armor={healthInfo.InitialArmor}, " +
                        $"Damage={healthInfo.Damage}, " +
                        $"Total Health={healthInfo.TotalHealth}"
                    );

                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"OnTurnStart - Checking all hero data for , Tags: {string.Join(", ", heroEntity.Tags.Select(t => $"{t.Key}={t.Value}"))}");
                }
            }

            // Only add if we don't already have data for this turn
            if (!_turns.Any(t => t.Turn == currentTurn))
            {
                _turns.Add(turnData);
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info(
                    $"Added turn data for turn {currentTurn}: " +
                    $"Minions={turnData.NumMinionsPlayedThisTurn}, " +
                    $"Spells={turnData.NumSpellsPlayedThisGame}, " +
                    $"Resources={turnData.NumResourcesSpentThisGame}, " +
                    $"Players tracked={turnData.PlayerHealths.Count}"
                );
            }
            else
            {
                // Update existing turn data
                var existingTurn = _turns.First(t => t.Turn == currentTurn);
                existingTurn.NumMinionsPlayedThisTurn = turnData.NumMinionsPlayedThisTurn;
                existingTurn.NumSpellsPlayedThisGame = turnData.NumSpellsPlayedThisGame;
                existingTurn.NumResourcesSpentThisGame = turnData.NumResourcesSpentThisGame;
                existingTurn.PlayerHealths = turnData.PlayerHealths;
            }
            _triplesCreated = playerEntity.GetTag(GameTag.PLAYER_TRIPLES);
            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"OnTurnStart - Player Entity found, Triples: {_triplesCreated}, Tags: {string.Join(", ", playerEntity.Tags.Select(t => $"{t.Key}={t.Value}"))}");
        }

        private void OnPlayerCreateInPlay(Card card)
        {
            if (Core.Game.CurrentGameMode != GameMode.Battlegrounds)
                return;

            // Log all cards created in play to help debug
            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"OnPlayerCreateInPlay: Name={card.Name}, Id={card.Id}, Type={card.Type}, Race={card.Race}, Set={card.Set}, Rarity={card.Rarity}");
            
            // Track anomaly if one is created
            if (card.Type == "Battleground_Anomaly")
            {
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Anomaly detected: {card.Name} ({card.Id})");
                _currentAnomalyId = card.Id;
                _currentAnomalyName = card.Name;
            }
        }

        private void OnGameStart()
        {
            if (Core.Game.CurrentGameMode == GameMode.Battlegrounds)
            {
                _triplesCreated = 0;
                _currentHeroId = null;
                _currentHeroName = null;
                gameStartTime = DateTime.Now;
                
                // Get starting MMR from BattlegroundsRatingInfo
                var ratingInfo = Core.Game.BattlegroundsRatingInfo;
                if (ratingInfo != null)
                {
                    _startingMmr = ratingInfo.Rating;
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"OnGameStart - Set starting MMR to: {_startingMmr} from BattlegroundsRatingInfo");
                }
                else
                {
                    // Fallback to CurrentGameStats if BattlegroundsRatingInfo is not available
                    var stats = Core.Game.CurrentGameStats;
                    if (stats != null && stats.GameMode == GameMode.Battlegrounds)
                    {
                        _startingMmr = stats.BattlegroundsRating;
                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"OnGameStart - Set starting MMR to: {_startingMmr} from CurrentGameStats");
                    }
                }
                
                var hero = Core.Game.Player.Hero;
                if (hero != null)
                {
                    _currentHeroId = hero.CardId;
                    _currentHeroName = hero.Card?.Name;
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"OnGameStart - Found hero: {_currentHeroName} ({_currentHeroId})");
                }
            }
        }

        private void OnEntityWillTakeDamage(PredamageInfo predamageInfo)
        {
            if (!Core.Game.IsBattlegroundsMatch || !_isInCombat)
                return;
            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Damage dealt to opponent: {predamageInfo.Value}, Opponent: {predamageInfo.Entity.Name}");
        }

        private void ParseHDTLog()
        {
            try
            {
                var hdtLogPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HearthstoneDeckTracker",
                    "Logs",
                    "hdt_log.txt"
                );

                if (!File.Exists(hdtLogPath))
                {
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"HDT log file not found at: {hdtLogPath}");
                    return;
                }

                // Try up to 3 times with a small delay between attempts
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        string[] logLines;
                        using (var fileStream = new FileStream(hdtLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var reader = new StreamReader(fileStream))
                        {
                            logLines = reader.ReadToEnd().Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                        }

                        var currentTurn = 0;
                        var currentOpponentId = "";
                        var currentOpponentName = "";
                        var currentSimulationId = "";
                        var lastSimulationResults = new Dictionary<string, (double WinRate, double TheirDeathRate, double TieRate, double LossRate, double MyDeathRate)>();

                        foreach (var line in logLines)
                        {
                            if (line.Contains("OnTurnStart - Turn"))
                            {
                                var turnMatch = Regex.Match(line, @"Turn (\d+)");
                                if (turnMatch.Success)
                                {
                                    currentTurn = int.Parse(turnMatch.Groups[1].Value);
                                }
                            }
                            else if (line.Contains("BattlegroundsBoardState.SnapshotCurrentBoard"))
                            {
                                var opponentMatch = OpponentSnapshotRegex.Match(line);
                                if (opponentMatch.Success)
                                {
                                    currentOpponentName = opponentMatch.Groups[1].Value;
                                    currentOpponentId = opponentMatch.Groups[2].Value;
                                    
                                    // Store opponent ID in turn data
                                    var turnData = _turns.FirstOrDefault(t => t.Turn == currentTurn);
                                    if (turnData != null)
                                    {
                                        turnData.OpponentId = currentOpponentId;
                                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Found opponent for turn {currentTurn}: {currentOpponentName} (ID: {currentOpponentId})");
                                    }
                                }
                            }
                            else if (line.Contains("BobsBuddyInvoker.StartCombat"))
                            {
                                var simulationIdMatch = Regex.Match(line, @"BobsBuddyInvoker.StartCombat >> ([\w-]+)");
                                if (simulationIdMatch.Success)
                                {
                                    currentSimulationId = simulationIdMatch.Groups[1].Value;
                                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Starting combat simulation {currentSimulationId} on turn {currentTurn}");
                                }
                            }
                            else if (line.Contains("BobsBuddyInvoker.ValidateSimulationResultAsync"))
                            {
                                var validationMatch = CombatValidationRegex.Match(line);
                                if (validationMatch.Success)
                                {
                                    var result = validationMatch.Groups[1].Value;
                                    var lethalResult = validationMatch.Groups[2].Value;

                                    // Get or create turn data
                                    var turnData = _turns.FirstOrDefault(t => t.Turn == currentTurn);
                                    if (turnData == null)
                                    {
                                        turnData = new TurnData { Turn = currentTurn };
                                        _turns.Add(turnData);
                                    }

                                    turnData.ActualCombatResult = result;
                                    turnData.ActualLethalResult = lethalResult;

                                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info(
                                        $"Turn {currentTurn} combat result: {result}, Lethal: {lethalResult}"
                                    );
                                }
                            }
                            else if (line.Contains("WinRate=") && line.Contains("TieRate=") && line.Contains("LossRate="))
                            {
                                var match = SimulationResultRegex.Match(line);
                                if (match.Success && !string.IsNullOrEmpty(currentSimulationId))
                                {
                                    var winRate = double.Parse(match.Groups[1].Value);
                                    var theirDeathRate = double.Parse(match.Groups[2].Value);
                                    var tieRate = double.Parse(match.Groups[3].Value);
                                    var lossRate = double.Parse(match.Groups[4].Value);
                                    var myDeathRate = double.Parse(match.Groups[5].Value);

                                    lastSimulationResults[currentSimulationId] = (winRate, theirDeathRate, tieRate, lossRate, myDeathRate);

                                    // Get or create turn data
                                    var turnData = _turns.FirstOrDefault(t => t.Turn == currentTurn);
                                    if (turnData == null)
                                    {
                                        turnData = new TurnData { Turn = currentTurn };
                                        _turns.Add(turnData);
                                    }

                                    // Update simulation results
                                    turnData.WinRate = winRate;
                                    turnData.TheirDeathRate = theirDeathRate;
                                    turnData.TieRate = tieRate;
                                    turnData.LossRate = lossRate;
                                    turnData.MyDeathRate = myDeathRate;

                                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info(
                                        $"Updated turn {currentTurn} with simulation results: " +
                                        $"Win={turnData.WinRate}% (Lethal={turnData.TheirDeathRate}%), " +
                                        $"Tie={turnData.TieRate}%, " +
                                        $"Loss={turnData.LossRate}% (Lethal={turnData.MyDeathRate}%)"
                                    );
                                }
                            }
                        }

                        // If we successfully read the file, break out of the retry loop
                        break;
                    }
                    catch (IOException) when (attempt < 3)
                    {
                        // If this isn't our last attempt, wait a bit and try again
                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Failed to read HDT log on attempt {attempt}, retrying...");
                        System.Threading.Thread.Sleep(100 * attempt); // Increasing delay with each attempt
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Error($"Error parsing HDT log: {ex}");
            }
        }

        private void OnGameEnd()
        {
            if (Core.Game.CurrentGameMode != GameMode.Battlegrounds)
                return;

            try
            {
                // Wait 3 seconds to ensure MMR has updated
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(3000);
                        ParseHDTLog();

                        var stats = Core.Game.CurrentGameStats;
                        if (stats == null)
                        {
                            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("OnGameEnd: CurrentGameStats is null");
                            return;
                        }

                        // Get final MMR from BattlegroundsRatingInfo first
                        var ratingInfo = Core.Game.BattlegroundsRatingInfo;
                        int finalMmr = 0;
                        if (ratingInfo != null)
                        {
                            finalMmr = ratingInfo.Rating;
                            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"OnGameEnd - Got final MMR from BattlegroundsRatingInfo: {finalMmr}");
                        }
                        else if (stats.BattlegroundsRating > 0)
                        {
                            finalMmr = stats.BattlegroundsRating;
                            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"OnGameEnd - Got final MMR from CurrentGameStats: {finalMmr}");
                        }
                        else
                        {
                            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("OnGameEnd - Could not get final MMR from either source");
                            return;
                        }

                        var playerEntity = Core.Game.Entities.Values
                            .FirstOrDefault(x => x.IsPlayer);
                        if (playerEntity == null)
                        {
                            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("OnGameEnd: Player entity not found");
                            return;
                        }

                        // Debug logging for all relevant data
                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("=== OnGameEnd Debug Information ===");
                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Game Mode: {Core.Game.CurrentGameMode}");
                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Player Name: {Core.Game.Player.Name}");
                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"MMR - Starting: {_startingMmr}, Current: {stats.BattlegroundsRating}");
                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Hero - Current: {_currentHeroId ?? "null"}, Name: {_currentHeroName ?? "null"}");
                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Hero from Core - Id: {Core.Game.Player.Hero?.CardId ?? "null"}, Name: {Core.Game.Player.Hero?.Card?.LocalizedName ?? "null"}");
                        
                        // Log all entities to help debug placement
                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("=== All Game Entities ===");
                        foreach (var entity in Core.Game.Entities.Values)
                        {
                            if (entity.HasTag(GameTag.PLAYER_LEADERBOARD_PLACE) || entity.HasTag(GameTag.PLAYER_TRIPLES))
                            {
                                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Entity {entity.Id}: Name={entity.Name}, IsPlayer={entity.IsPlayer}, Place={entity.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE)}, Triples={entity.GetTag(GameTag.PLAYER_TRIPLES)}");
                            }
                        }

                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Player Entity Tags:");
                        foreach (var tag in playerEntity.Tags)
                        {
                            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"  {tag.Key} = {tag.Value}");
                        }

                        // Get placement - first try PLAYER_LEADERBOARD_PLACE, fallback to other methods
                        int placement = 0;
                        
                        // Try to get placement from any entity that has our player ID and a leaderboard place
                        var playerPlaceEntity = Core.Game.Entities.Values
                            .FirstOrDefault(e => e.GetTag(GameTag.PLAYER_ID) == playerEntity.GetTag(GameTag.PLAYER_ID) 
                                            && e.HasTag(GameTag.PLAYER_LEADERBOARD_PLACE));
                        
                        if (playerPlaceEntity != null)
                        {
                            placement = playerPlaceEntity.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE);
                            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Found placement from player entity: {placement}");
                        }
                        else if (playerEntity.HasTag(GameTag.PLAYER_LEADERBOARD_PLACE))
                        {
                            placement = playerEntity.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE);
                            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Found placement from PLAYER_LEADERBOARD_PLACE: {placement}");
                        }
                        else
                        {
                            // Count how many players are still alive
                            var alivePlayers = Core.Game.Entities.Values
                                .Count(e => e.HasTag(GameTag.PLAYER_LEADERBOARD_PLACE) && e.GetTag(GameTag.PLAYSTATE) != (int)PlayState.CONCEDED);
                            placement = alivePlayers + 1;
                            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Calculated placement from alive players: {placement}");
                        }

                        // Get triples count - check all entities controlled by the player
                        var triplesCreated = Core.Game.Entities.Values
                            .Where(e => e.IsControlledBy(playerEntity.GetTag(GameTag.CONTROLLER)))
                            .Max(e => e.GetTag(GameTag.PLAYER_TRIPLES));

                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Found triples count: {triplesCreated}");

                        // Get final board state
                        var finalBoard = new List<BoardMinion>();
                        var playerBoardEntities = Core.Game.Entities.Values
                            .Where(e => e.IsInPlay && e.IsMinion && e.IsControlledBy(playerEntity.GetTag(GameTag.CONTROLLER)))
                            .OrderBy(e => e.GetTag(GameTag.ZONE_POSITION));

                        foreach (var entity in playerBoardEntities)
                        {
                            var minion = new BoardMinion
                            {
                                CardId = entity.CardId,
                                Name = entity.Card?.LocalizedName ?? "Unknown",
                                Attack = entity.GetTag(GameTag.ATK),
                                Health = entity.GetTag(GameTag.HEALTH),
                                IsTaunt = entity.HasTag(GameTag.TAUNT),
                                IsDivineShield = entity.HasTag(GameTag.DIVINE_SHIELD),
                                IsReborn = entity.HasTag(GameTag.REBORN),
                            };

                            // Get enchantments
                            var enchantments = entity.GetTag(GameTag.ENCHANTMENT_BIRTH_VISUAL);
                            if (enchantments > 0)
                            {
                                minion.Enchantments = Core.Game.Entities.Values
                                    .Where(e => e.GetTag(GameTag.ATTACHED) == entity.Id && e.GetTag(GameTag.ENCHANTMENT_BIRTH_VISUAL) == enchantments)
                                    .Select(e => e.CardId)
                                    .ToList();
                            }

                            finalBoard.Add(minion);
                        }

                        // Log board state
                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("=== Final Board State ===");
                        foreach (var minion in finalBoard)
                        {
                            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info(
                                $"Minion: {minion.Name} ({minion.CardId}) - {minion.Attack}/{minion.Health}" +
                                $"{(minion.IsTaunt ? " [Taunt]" : "")}" +
                                $"{(minion.IsDivineShield ? " [Divine Shield]" : "")}" +
                                $"{(minion.IsReborn ? " [Reborn]" : "")}" +
                                $"{(minion.IsPoisonous ? " [Poisonous]" : "")}" +
                                $"{(minion.isVenomous ? " [Venomous]" : "")}" +
                                $"{(minion.Enchantments.Any() ? $" [Enchantments: {string.Join(", ", minion.Enchantments)}]" : "")}"
                            );
                        }

                        var gameData = new BattlegroundsGameData
                        {
                            PlayerIdentifier = Core.Game.Player.Name,
                            Placement = placement,
                            StartingMmr = _startingMmr,
                            FinalMmr = finalMmr,
                            MmrGained = finalMmr - _startingMmr,
                            GameDurationInSeconds = (int)(DateTime.Now - gameStartTime).TotalSeconds,
                            GameEndDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ff"),
                            HeroPlayed = BattlegroundsUtils.GetOriginalHeroId(_currentHeroId) ?? "Unknown",
                            HeroPlayedName = _currentHeroName ?? "Unknown",
                            AnomalyId = _currentAnomalyId ?? "None",
                            AnomalyName = _currentAnomalyName ?? "None",
                            TriplesCreated = triplesCreated,
                            FinalBoard = finalBoard,
                            Turns = _turns,
                            Region = GetRegionStr(),
                        };

                        // Log the final game data
                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("=== Final Game Data ===");
                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info(JsonConvert.SerializeObject(gameData, Formatting.Indented));

                        // Save game data to file
                        var bgGamesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BGGames");
                        
                        // Ensure directory exists
                        if (!Directory.Exists(bgGamesDir))
                        {
                            Directory.CreateDirectory(bgGamesDir);
                        }

                        var fileName = $"BGGame_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                        var fullPath = Path.Combine(bgGamesDir, fileName);
                        
                        // Save the file first
                        File.WriteAllText(fullPath, JsonConvert.SerializeObject(gameData, Formatting.Indented));

                        // Log successful write
                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"BG game data saved to: {fullPath}");

                        // Clear simulation results for next game
                        _turns.Clear();
                    }
                    catch (Exception ex)
                    {
                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Error($"Failed to save BG game data: {ex.Message}");
                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Error($"Stack trace: {ex.StackTrace}");
                    }
                });
            }
            catch (Exception ex)
            {
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Error($"Error in OnGameEnd: {ex}");
            }
        }

        public void Dispose()
        {
            // HDT's ActionList doesn't support Remove, so we'll just let the events be cleaned up when the plugin is unloaded
        }

        private string GetRegionStr()
        {
            switch (Core.Game.CurrentRegion)
            {
                case Region.US:
                    return "US";
                case Region.EU:
                    return "EU";
                default:
                    return "AP";
            }
        }
    }
}