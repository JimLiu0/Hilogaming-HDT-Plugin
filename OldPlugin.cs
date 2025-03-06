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

    public enum TurnPhase 
    { 
        PlayerTurn, 
        OpponentTurn 
    }

    public class SimulationData
    {
        public double WinRate { get; set; }
        public double TheirDeathRate { get; set; }
        public double TieRate { get; set; }
        public double LossRate { get; set; }
        public double MyDeathRate { get; set; }
    }

    public class CombatResultData
    {
        public string ActualCombatResult { get; set; }
        public string ActualLethalResult { get; set; }
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
        public int TavernTier { get; set; }
        [JsonIgnore]
        public List<PlayerHealthInfo> PlayerHealths { get; set; } = new List<PlayerHealthInfo>();
        [JsonIgnore]
        public List<PlayerHealthInfo> PreCombatHealths { get; set; } = new List<PlayerHealthInfo>();
        public int HeroDamage { get; set; } // Positive if we dealt damage, negative if we took damage, 0 for tie
        public string OpponentId { get; set; } // Store opponent's ID for this turn
        public TurnPhase Phase { get; set; }
        public bool HasSimulationResults { get; set; }
        public bool HasCombatResults { get; set; }

        public TurnData(int turn, TurnPhase phase)
        {
            Turn = turn;
            Phase = phase;
            // Initialize with default values
            WinRate = 0.0;
            TieRate = 0.0;
            LossRate = 0.0;
            TheirDeathRate = 0.0;
            MyDeathRate = 0.0;
            NumMinionsPlayedThisTurn = 0;
            NumSpellsPlayedThisGame = 0;
            NumResourcesSpentThisGame = 0;
            TavernTier = 1;
            HeroDamage = 0;
            HasSimulationResults = false;
            HasCombatResults = false;
        }

        public void UpdateSimulationResults(SimulationData simData)
        {
            WinRate = simData.WinRate;
            TheirDeathRate = simData.TheirDeathRate;
            TieRate = simData.TieRate;
            LossRate = simData.LossRate;
            MyDeathRate = simData.MyDeathRate;
            HasSimulationResults = true;
        }

        public void UpdateCombatResults(CombatResultData combatData)
        {
            ActualCombatResult = combatData.ActualCombatResult;
            ActualLethalResult = combatData.ActualLethalResult;
            HasCombatResults = true;
        }

        public void CapturePreCombatHealth(List<PlayerHealthInfo> healthInfos)
        {
            if (Phase == TurnPhase.OpponentTurn)
            {
                PreCombatHealths = healthInfos.Select(h => new PlayerHealthInfo
                {
                    PlayerId = h.PlayerId,
                    PlayerName = h.PlayerName,
                    HeroCardId = h.HeroCardId,
                    HeroName = h.HeroName,
                    Health = h.Health,
                    CurrentArmor = h.CurrentArmor,
                    InitialArmor = h.InitialArmor,
                    Damage = h.Damage
                }).ToList();
            }
        }
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

    public class HiloTurnData
    {
        public int turn { get; set; }
        public int heroDamage { get; set; }
        public double winOdds { get; set; }
        public double tieOdds { get; set; }
        public double lossOdds { get; set; }
        public double averageDamageTaken { get; set; }
        public double averageDamageDealt { get; set; }
        // Additional turn data
        public string actualCombatResult { get; set; }
        public string actualLethalResult { get; set; }
        public int numMinionsPlayedThisTurn { get; set; }
        public int numSpellsPlayedThisGame { get; set; }
        public int numResourcesSpentThisGame { get; set; }
        public int tavernTier { get; set; }
    }

    public class HiloBoardMinion
    {
        public string cardID { get; set; }
        public int id { get; set; }
        public Dictionary<string, int> tags { get; set; }
    }

    public class HiloFinalComp
    {
        public List<HiloBoardMinion> board { get; set; }
    }

    public class HiloGameData
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
        public List<HiloTurnData> turns { get; set; }
        public HiloFinalComp finalComp { get; set; }
    }

    public class BattlegroundsGameCollection : IDisposable
    {
        private DateTime gameStartTime;
        private int _triplesCreated;
        private string _currentHeroId;
        private string _currentHeroName;
        private string _currentAnomalyId;
        private string _currentAnomalyName;
        private List<TurnData> _turns = new List<TurnData>();
        private static readonly Regex SimulationResultRegex = new Regex(@"WinRate=(\d+(?:\.\d+)?)% \(Lethal=(\d+(?:\.\d+)?)%\), TieRate=(\d+(?:\.\d+)?)%, LossRate=(\d+(?:\.\d+)?)% \(Lethal=(\d+(?:\.\d+)?)%\)");
        private static readonly Regex OpponentSnapshotRegex = new Regex(@"BattlegroundsBoardState\.SnapshotCurrentBoard >> Snapshotting board state for (.*?) with player id (\d+)");
        private static readonly Regex CombatValidationRegex = new Regex(@"BobsBuddyInvoker\.ValidateSimulationResultAsync >> result=(\w+), lethalResult=(\w+)");
        
        private static readonly HttpClient httpClient = new HttpClient();
        private const string DEV_API_URL = "https://hilo-backend.azurewebsites.net/api/hearthstone-battlegrounds/submit-game-data/";
        private const string PROD_API_URL = "https://hilo-production.azurewebsites.net/api/hearthstone-battlegrounds/submit-game-data/";

        private int _currentPlacement;
        private List<BoardMinion> _finalBoard = new List<BoardMinion>();

        public BattlegroundsGameCollection()
        {
            GameEvents.OnGameStart.Add(() => OnGameStart().GetAwaiter().GetResult());
            GameEvents.OnGameEnd.Add(OnGameEnd);
            GameEvents.OnTurnStart.Add(OnTurnStart);
            GameEvents.OnPlayerCreateInPlay.Add(OnPlayerCreateInPlay);
        }

        private void OnTurnStart(ActivePlayer player)
        {
            if (Core.Game.CurrentGameMode != GameMode.Battlegrounds)
                return;

            // Log current game state
            var currentTurn = Core.Game.GetTurnNumber();
            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"OnTurnStart - Turn {currentTurn}, Player: {player}");

            // Update hero information from Core.Game.Player.Hero
            var hero = Core.Game.Player.Hero;
            if (hero != null)
            {
                _currentHeroId = hero.CardId;
                _currentHeroName = hero.Card?.LocalizedName;
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"OnTurnStart - Hero: {_currentHeroName} ({_currentHeroId})");
            }

            // Get all player entities by leaderboard place
            var playerEntities = Core.Game.Entities.Values.Where(x => x.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE) != 0).ToList();
            var playerEntity = Core.Game.Entities.Values.FirstOrDefault(x => x.IsPlayer);

            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Found {playerEntities.Count} player entities with leaderboard places");

            // Determine the current phase based on the active player
            var currentPhase = player == ActivePlayer.Player ? TurnPhase.PlayerTurn : TurnPhase.OpponentTurn;
            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Turn {currentTurn} Phase: {currentPhase}");

            // Get or create current turn data
            var turnData = _turns.FirstOrDefault(t => t.Turn == currentTurn);
            if (turnData == null)
            {
                turnData = new TurnData(currentTurn, currentPhase);
                _turns.Add(turnData);
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Created new turn data for turn {currentTurn} with phase {currentPhase}");
            }
            else
            {
                // Update phase if this is a phase change within the same turn
                turnData.Phase = currentPhase;
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Updated existing turn {currentTurn} to phase {currentPhase}");
            }

            // Process player health info
            turnData.PlayerHealths.Clear(); // Clear existing health info before updating
            foreach (var pey in playerEntities)
            {
                var playerId = pey.GetTag(GameTag.PLAYER_ID);
                var playerName = Core.Game.Entities.Values
                    .FirstOrDefault(e => e.HasTag(GameTag.PLAYER_ID) && e.GetTag(GameTag.PLAYER_ID) == playerId && e.HasTag(GameTag.PLAYSTATE))
                    ?.GetTag(GameTag.PLAYER_ID)
                    .ToString() ?? "Unknown";

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
                }
            }

            // If this is the opponent's turn, capture pre-combat health
            if (currentPhase == TurnPhase.OpponentTurn)
            {
                turnData.CapturePreCombatHealth(turnData.PlayerHealths);
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Captured pre-combat health for turn {currentTurn}");
            }

            // Set opponent ID from the next opponent player ID tag
            if (playerEntity != null)
            {
                var nextOpponentId = playerEntity.GetTag(GameTag.NEXT_OPPONENT_PLAYER_ID);
                if (nextOpponentId > 0)
                {
                    turnData.OpponentId = nextOpponentId.ToString();
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Set opponent ID for turn {currentTurn} to {nextOpponentId}");
                }
            }

            // Only update resource counts during player turn
            if (currentPhase == TurnPhase.PlayerTurn && playerEntity != null)
            {
                turnData.NumMinionsPlayedThisTurn = playerEntity.GetTag(GameTag.NUM_MINIONS_PLAYED_THIS_TURN);
                turnData.NumSpellsPlayedThisGame = playerEntity.GetTag(GameTag.NUM_SPELLS_PLAYED_THIS_GAME);
                turnData.NumResourcesSpentThisGame = playerEntity.GetTag(GameTag.NUM_RESOURCES_SPENT_THIS_GAME);
                turnData.TavernTier = playerEntity.GetTag(GameTag.PLAYER_TECH_LEVEL);

                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info(
                    $"Updated resource counts for turn {currentTurn}: " +
                    $"Minions={turnData.NumMinionsPlayedThisTurn}, " +
                    $"Spells={turnData.NumSpellsPlayedThisGame}, " +
                    $"Resources={turnData.NumResourcesSpentThisGame}, " +
                    $"TavernTier={turnData.TavernTier}");
            }

            // Update triples count
            _triplesCreated = playerEntity?.GetTag(GameTag.PLAYER_TRIPLES) ?? 0;
            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Updated triples count: {_triplesCreated}");

            var previousTurn = _turns[currentTurn - 2];

            // Calculate hero damage if this is the opponent's turn and we have pre-combat health values
            if (currentPhase == TurnPhase.OpponentTurn && previousTurn != null && previousTurn.PreCombatHealths.Any())
            {
                var ourPreviousPreCombatHealth = previousTurn.PreCombatHealths
                    .FirstOrDefault(p => p.PlayerId == Core.Game.Player.Id.ToString());
                var ourCurrentHealth = turnData.PlayerHealths
                    .FirstOrDefault(p => p.PlayerId == Core.Game.Player.Id.ToString());
                var theirPreviousPreCombatHealth = previousTurn.PreCombatHealths
                    .FirstOrDefault(p => p.PlayerId == previousTurn.OpponentId);
                var theirCurrentHealth = turnData.PlayerHealths
                    .FirstOrDefault(p => p.PlayerId == previousTurn.OpponentId);

                if (ourPreviousPreCombatHealth != null && ourCurrentHealth != null && 
                    theirPreviousPreCombatHealth != null && theirCurrentHealth != null)
                {
                    var ourHealthDiff = ourPreviousPreCombatHealth.TotalHealth - ourCurrentHealth.TotalHealth;
                    var theirHealthDiff = theirPreviousPreCombatHealth.TotalHealth - theirCurrentHealth.TotalHealth;

                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info(
                        $"=== Turn {currentTurn} Damage Calculation ===" +
                        $"\nOur Pre-Combat Health: {ourPreviousPreCombatHealth.TotalHealth}" +
                        $"\nOur Current Health: {ourCurrentHealth.TotalHealth}" +
                        $"\nOur Health Change: {ourHealthDiff}" +
                        $"\nTheir Pre-Combat Health: {theirPreviousPreCombatHealth.TotalHealth}" +
                        $"\nTheir Current Health: {theirCurrentHealth.TotalHealth}" +
                        $"\nTheir Health Change: {theirHealthDiff}");

                    if (theirHealthDiff > 0)
                    {
                        previousTurn.HeroDamage = theirHealthDiff;
                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"We dealt {theirHealthDiff} damage to opponent");
                    }
                    else if (ourHealthDiff > 0)
                    {
                        previousTurn.HeroDamage = -ourHealthDiff;
                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"We took {ourHealthDiff} damage from opponent");
                    }
                    else
                    {
                        previousTurn.HeroDamage = 0;
                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("No health changes - likely a tie");
                    }
                }
                else
                {
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info(
                        $"Missing health info for damage calculation:" +
                        $"\nOur Pre-Combat Health Info: {(ourPreviousPreCombatHealth != null ? "Found" : "Missing")}" +
                        $"\nOur Current Health Info: {(ourCurrentHealth != null ? "Found" : "Missing")}" +
                        $"\nTheir Pre-Combat Health Info: {(theirPreviousPreCombatHealth != null ? "Found" : "Missing")}" +
                        $"\nTheir Current Health Info: {(theirCurrentHealth != null ? "Found" : "Missing")}");
                }
            }
        }

        private void OnPlayerCreateInPlay(Card card)
        {
            if (Core.Game.CurrentGameMode != GameMode.Battlegrounds)
                return;

            // // Log all cards created in play to help debug
            // Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"OnPlayerCreateInPlay: Name={card.Name}, Id={card.Id}, Type={card.Type}, Race={card.Race}, Set={card.Set}, Rarity={card.Rarity}");
            
            // Track anomaly if one is created
            if (card.Type == "Battleground_Anomaly")
            {
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Anomaly detected: {card.Name} ({card.Id})");
                _currentAnomalyId = card.Id;
                _currentAnomalyName = card.Name;
            }
        }

        private async Task OnGameStart()
        {
            if (Core.Game.CurrentGameMode == GameMode.Battlegrounds)
            {
                _triplesCreated = 0;
                _currentHeroId = null;
                _currentHeroName = null;
                gameStartTime = DateTime.Now;
                _turns.Clear(); // Clear any existing turn data
                
                // Initialize first turn data
                var playerEntity = Core.Game.Entities.Values.FirstOrDefault(x => x.IsPlayer);
                if (playerEntity != null)
                {
                    var turnData = new TurnData(1, TurnPhase.PlayerTurn)
                    {
                        NumMinionsPlayedThisTurn = 0,
                        NumSpellsPlayedThisGame = 0,
                        NumResourcesSpentThisGame = 0,
                        WinRate = 0.0,
                        TieRate = 0.0,
                        LossRate = 0.0,
                        TheirDeathRate = 0.0,
                        MyDeathRate = 0.0,
                        HeroDamage = 0
                    };
                    _turns.Add(turnData);
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

        private void OnInMenu()
        {
            if (Core.Game.CurrentGameMode == GameMode.Battlegrounds)
            {
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("OnInMenu - MMR: " + Core.Game.BattlegroundsRatingInfo.Rating);
            }
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
                            // Read only the last portion of the file to improve performance
                            var maxBytesToRead = 500000; // Read last 500KB
                            var buffer = new char[maxBytesToRead];
                            var startPosition = Math.Max(0, fileStream.Length - maxBytesToRead);
                            fileStream.Seek(startPosition, SeekOrigin.Begin);
                            reader.Read(buffer, 0, maxBytesToRead);
                            logLines = new string(buffer).Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                        }

                        // Find the index of the most recent game start
                        int gameStartIndex = -1;
                        for (int i = logLines.Length - 1; i >= 0; i--)
                        {
                            if (logLines[i].Contains("GameEventHandler.HandleGameStart >> --- Game start ---"))
                            {
                                gameStartIndex = i;
                                break;
                            }
                        }

                        if (gameStartIndex == -1)
                        {
                            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("Could not find game start marker in log file");
                            return;
                        }

                        var currentTurn = Core.Game.GetTurnNumber();
                        var currentOpponentId = "";
                        var currentOpponentName = "";
                        var currentSimulationId = "";
                        var simulationStartTime = DateTime.MinValue;
                        var turnStartTimes = new Dictionary<int, DateTime>();
                        var simulationsPerTurn = new Dictionary<int, int>();

                        // Process the log lines in chronological order, but only after game start
                        for (int i = gameStartIndex; i < logLines.Length; i++)
                        {
                            var line = logLines[i];
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            if (line.Contains("OnTurnStart - Turn"))
                            {
                                var turnMatch = Regex.Match(line, @"Turn (\d+)");
                                if (turnMatch.Success)
                                {
                                    var turnNumber = int.Parse(turnMatch.Groups[1].Value);
                                    currentTurn = turnNumber;
                                    turnStartTimes[turnNumber] = DateTime.Now;
                                    simulationsPerTurn[turnNumber] = 0;
                                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Found turn start marker for turn {turnNumber}");
                                }
                            }
                            else if (line.Contains("BattlegroundsBoardState.SnapshotCurrentBoard"))
                            {
                                var opponentMatch = OpponentSnapshotRegex.Match(line);
                                if (opponentMatch.Success)
                                {
                                    currentOpponentName = opponentMatch.Groups[1].Value;
                                    currentOpponentId = opponentMatch.Groups[2].Value;
                                    
                                    var turnData = _turns.FirstOrDefault(t => t.Turn == currentTurn);
                                    if (turnData != null)
                                    {
                                        turnData.OpponentId = currentOpponentId;
                                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Found opponent for turn {currentTurn}: {currentOpponentName} (ID: {currentOpponentId})");
                                    }
                                }
                            }
                            else if (line.Contains("BobsBuddyInvoker.RunAndDisplaySimulationAsync >> Running simulation"))
                            {
                                simulationStartTime = DateTime.Now;
                                if (!simulationsPerTurn.ContainsKey(currentTurn))
                                {
                                    simulationsPerTurn[currentTurn] = 0;
                                }
                                simulationsPerTurn[currentTurn] += 1;
                                if (!simulationsPerTurn.ContainsKey(currentTurn))
                                {
                                    simulationsPerTurn[currentTurn] = 0;
                                }
                                simulationsPerTurn[currentTurn] += 1;
                                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Found simulation start for turn {currentTurn} (#{simulationsPerTurn[currentTurn]})");
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

                                    // Update the current turn's combat results
                                    var turnData = _turns.FirstOrDefault(t => t.Turn == currentTurn);
                                    if (turnData != null)
                                    {
                                        var combatData = new CombatResultData
                                        {
                                            ActualCombatResult = result,
                                            ActualLethalResult = lethalResult
                                        };
                                        turnData.UpdateCombatResults(combatData);

                                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info(
                                            $"Turn {currentTurn} combat validation: " +
                                            $"Result={result}, Lethal={lethalResult}, " +
                                            $"SimulationId={currentSimulationId}"
                                        );
                                    }
                                }
                            }
                            else if (line.Contains("WinRate=") && line.Contains("TieRate=") && line.Contains("LossRate="))
                            {
                                var match = SimulationResultRegex.Match(line);
                                if (match.Success)
                                {
                                    var simData = new SimulationData
                                    {
                                        WinRate = double.Parse(match.Groups[1].Value),
                                        TheirDeathRate = double.Parse(match.Groups[2].Value),
                                        TieRate = double.Parse(match.Groups[3].Value),
                                        LossRate = double.Parse(match.Groups[4].Value),
                                        MyDeathRate = double.Parse(match.Groups[5].Value)
                                    };

                                    // Update the current turn's simulation results
                                    var turnData = _turns.FirstOrDefault(t => t.Turn == currentTurn);
                                    if (turnData != null)
                                    {
                                        turnData.UpdateSimulationResults(simData);

                                        var simulationDuration = DateTime.Now - simulationStartTime;
                                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info(
                                            $"Turn {currentTurn} simulation results: " +
                                            $"Win={simData.WinRate}% (Lethal={simData.TheirDeathRate}%), " +
                                            $"Tie={simData.TieRate}%, " +
                                            $"Loss={simData.LossRate}% (Lethal={simData.MyDeathRate}%), " +
                                            $"Duration={simulationDuration.TotalSeconds:F1}s"
                                        );
                                    }
                                }
                            }
                        }

                        // Log simulation statistics
                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("=== Simulation Statistics ===");
                        foreach (var kvp in simulationsPerTurn.OrderBy(x => x.Key))
                        {
                            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Turn {kvp.Key}: {kvp.Value} simulation(s)");
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

        private async Task SendGameDataToServers(HiloGameData gameData)
        {
            try
            {
                var jsonContent = JsonConvert.SerializeObject(gameData, Formatting.Indented);
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"=== Game Data JSON Content ===\n{jsonContent}");

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
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Saved game data to: {fullPath}");
            }
            catch (Exception ex)
            {
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Error($"Error saving game data: {ex.Message}\nStack Trace: {ex.StackTrace}");
            }
        }

        private HiloGameData TransformToHiloFormat(BattlegroundsGameData gameData)
        {
            var hiloData = new HiloGameData
            {
                playerIdentifier = gameData.PlayerIdentifier,
                placement = gameData.Placement,
                startingMmr = gameData.StartingMmr,
                mmrGained = gameData.MmrGained,
                gameDurationInSeconds = gameData.GameDurationInSeconds,
                gameEndDate = gameData.GameEndDate,
                heroPlayed = gameData.HeroPlayed,
                heroPlayedName = gameData.HeroPlayedName,
                triplesCreated = gameData.TriplesCreated,
                battleLuck = 0.0, // TODO: Calculate battle luck if needed
                server = $"REGION_{gameData.Region}",
                turns = gameData.Turns.Select(t => new HiloTurnData
                {
                    turn = t.Turn,
                    heroDamage = t.HeroDamage,
                    winOdds = t.WinRate,
                    tieOdds = t.TieRate,
                    lossOdds = t.LossRate,
                    averageDamageTaken = 0.0,
                    averageDamageDealt = 0.0,
                    // Additional turn data
                    actualCombatResult = t.ActualCombatResult,
                    actualLethalResult = t.ActualLethalResult,
                    numMinionsPlayedThisTurn = t.NumMinionsPlayedThisTurn,
                    numSpellsPlayedThisGame = t.NumSpellsPlayedThisGame,
                    numResourcesSpentThisGame = t.NumResourcesSpentThisGame,
                    tavernTier = t.TavernTier
                }).ToList(),
                finalComp = new HiloFinalComp
                {
                    board = gameData.FinalBoard.Select((m, index) => new HiloBoardMinion
                    {
                        cardID = m.CardId,
                        id = 10000 + index,
                        tags = new Dictionary<string, int>
                        {
                            { "ATK", m.Attack },
                            { "HEALTH", m.Health },
                            { "TAUNT", m.IsTaunt ? 1 : 0 },
                            { "DIVINE_SHIELD", m.IsDivineShield ? 1 : 0 },
                            { "REBORN", m.IsReborn ? 1 : 0 },
                            { "POISONOUS", m.IsPoisonous ? 1 : 0 },
                            { "VENOMOUS", m.isVenomous ? 1 : 0 }
                        }
                    }).ToList()
                }
            };

            return hiloData;
        }

        private async Task CalculateAndUpdateMmr()
        {
            try
            {
                // Wait 5 seconds to ensure MMR has been updated
                await Task.Delay(5000);

                var instance = BattlegroundsLastGames.Instance;
                var latestGame = instance.Games.Last();
                int ratingAfter = latestGame.RatingAfter;
                int ratingBefore = latestGame.Rating;
                int mmrChange = ratingAfter - ratingBefore;

                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info(
                    $"=== MMR Calculation (after 5s delay) ===" +
                    $"\nRating Before: {ratingBefore}" +
                    $"\nRating After: {ratingAfter}" +
                    $"\nMMR Change: {mmrChange}");

                // Create and send game data with updated MMR
                var gameData = new BattlegroundsGameData
                {
                    PlayerIdentifier = Core.Game.Player.Name,
                    Placement = _currentPlacement,
                    StartingMmr = ratingBefore,
                    FinalMmr = ratingAfter,
                    MmrGained = mmrChange,
                    GameDurationInSeconds = (int)(DateTime.Now - gameStartTime).TotalSeconds,
                    GameEndDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ff"),
                    HeroPlayed = BattlegroundsUtils.GetOriginalHeroId(_currentHeroId) ?? "Unknown",
                    HeroPlayedName = _currentHeroName ?? "Unknown",
                    AnomalyId = _currentAnomalyId ?? "None",
                    AnomalyName = _currentAnomalyName ?? "None",
                    TriplesCreated = _triplesCreated,
                    FinalBoard = _finalBoard,
                    Turns = _turns,
                    Region = GetRegionStr(),
                };

                // Transform and send data to servers
                var hiloData = TransformToHiloFormat(gameData);
                await SendGameDataToServers(hiloData);

                // Clear data for next game
                _turns.Clear();
            }
            catch (Exception ex)
            {
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Error($"Error calculating MMR: {ex}");
            }
        }

        private void OnGameEnd()
        {
            if (Core.Game.CurrentGameMode != GameMode.Battlegrounds)
                return;

            try
            {
                // Get all player entities and their health info at game end
                var playerEntities = Core.Game.Entities.Values.Where(x => x.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE) != 0).ToList();
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"=== Game End Player Health Information ===");
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Found {playerEntities.Count} player entities with leaderboard places");

                // Process player entities and update health info
                // ... [keep existing health info processing code] ...

                // Get player entity
                var playerEntity = Core.Game.Entities.Values.FirstOrDefault(x => x.IsPlayer);
                if (playerEntity == null)
                {
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("OnGameEnd: Player entity not found");
                    return;
                }

                // Calculate placement
                _currentPlacement = CalculatePlacement(playerEntity);

                // Get final board state
                _finalBoard = GetFinalBoardState(playerEntity);

                // Parse HDT log for final turn data
                ParseHDTLog();

                // Start async MMR calculation
                _ = CalculateAndUpdateMmr();
            }
            catch (Exception ex)
            {
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Error($"Error in OnGameEnd: {ex}");
            }
        }

        private int CalculatePlacement(Entity playerEntity)
        {
            // Try to get placement from any entity that has our player ID and a leaderboard place
            var playerPlaceEntity = Core.Game.Entities.Values
                .FirstOrDefault(e => e.GetTag(GameTag.PLAYER_ID) == playerEntity.GetTag(GameTag.PLAYER_ID) 
                                && e.HasTag(GameTag.PLAYER_LEADERBOARD_PLACE));
            
            if (playerPlaceEntity != null)
            {
                var placement1 = playerPlaceEntity.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE);
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Found placement from player entity: {placement1}");
                return placement1;
            }

            if (playerEntity.HasTag(GameTag.PLAYER_LEADERBOARD_PLACE))
            {
                var placement2 = playerEntity.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE);
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Found placement from PLAYER_LEADERBOARD_PLACE: {placement2}");
                return placement2;
            }

            // Count how many players are still alive
            var alivePlayers = Core.Game.Entities.Values
                .Count(e => e.HasTag(GameTag.PLAYER_LEADERBOARD_PLACE) && e.GetTag(GameTag.PLAYSTATE) != (int)PlayState.CONCEDED);
            var placement = alivePlayers + 1;
            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Calculated placement from alive players: {placement}");
            return placement;
        }

        private List<BoardMinion> GetFinalBoardState(Entity playerEntity)
        {
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

            return finalBoard;
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