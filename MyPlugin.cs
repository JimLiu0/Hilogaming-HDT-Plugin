using Hearthstone_Deck_Tracker.API;
using BattlegroundsGameCollection.Controls;
using BattlegroundsGameCollection.Properties;
using System;
using System.Windows.Controls;
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
using System.Windows;
using System.Collections.Generic;

namespace BattlegroundsGameCollection
{
    public class TurnData
    {
        public int Turn { get; set; }
        public double WinRate { get; set; }
        public double TieRate { get; set; }
        public double LossRate { get; set; }
        public double TheirDeathRate { get; set; }
        public double MyDeathRate { get; set; }
        public int NumMinionsPlayedThisTurn { get; set; }
        public int NumSpellsPlayedThisGame { get; set; }
        public int NumResourcesSpentThisGame { get; set; }
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
        public List<BoardMinion> FinalBoard { get; set; } = new List<BoardMinion>();
        public int TotalDamageDealt { get; set; }
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
        public List<string> Enchantments { get; set; } = new List<string>();
    }

    public class BattlegroundsGameCollection : IDisposable
    {
        private string panelName = "pluginStackPanelView";
        private TextBlock gameStartOverlay;
        private TextBlock gameEndOverlay;
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
        private Entity _attackingHero;
        private Entity _defendingHero;
        private Entity _lastAttackingHero;
        private int _lastAttackingHeroAttack;
        private List<TurnData> _turns = new List<TurnData>();
        private static readonly Regex SimulationResultRegex = new Regex(@"WinRate=(\d+(?:\.\d+)?)% \(Lethal=(\d+(?:\.\d+)?)%\), TieRate=(\d+(?:\.\d+)?)%, LossRate=(\d+(?:\.\d+)?)% \(Lethal=(\d+(?:\.\d+)?)%\)");

        public static InputMoveManager inputMoveManager;
        public PlugInDisplayControl stackPanel;

        public BattlegroundsGameCollection()
        {
            InitViewPanel();
            GameEvents.OnGameStart.Add(OnGameStart);
            GameEvents.OnGameEnd.Add(OnGameEnd);
            GameEvents.OnInMenu.Add(OnInMenu);
            GameEvents.OnTurnStart.Add(OnTurnStart);
            GameEvents.OnPlayerCreateInPlay.Add(OnPlayerCreateInPlay);
            
            // Track damage
            GameEvents.OnEntityWillTakeDamage.Add(OnEntityWillTakeDamage);
        }

        private void OnInMenu()
        {
            // Get starting MMR when in menu
            var stats = Core.Game.CurrentGameStats;
            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"OnInMenu - GameStats: {stats != null}, GameMode: {stats?.GameMode}, MMR: {stats?.BattlegroundsRating}");
            if (stats != null && stats.GameMode == GameMode.Battlegrounds)
            {
                _startingMmr = stats.BattlegroundsRating;
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"OnInMenu - Set starting MMR to: {_startingMmr}");
            }
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

            var opponent = Core.Game.Opponent.Hero;
            if (opponent != null)
            {
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"OnTurnStart - Opponent: {opponent.Card?.LocalizedName} ({opponent.CardId})");
            }

            // Track turn statistics
            var playerEntity = Core.Game.Entities.Values.FirstOrDefault(x => x.IsPlayer);
            if (playerEntity != null)
            {
                var currentTurn = Core.Game.GetTurnNumber();
                var turnData = new TurnData
                {
                    Turn = currentTurn,
                    NumMinionsPlayedThisTurn = playerEntity.GetTag(GameTag.NUM_MINIONS_PLAYED_THIS_TURN),
                    NumSpellsPlayedThisGame = playerEntity.GetTag(GameTag.NUM_SPELLS_PLAYED_THIS_GAME),
                    NumResourcesSpentThisGame = playerEntity.GetTag(GameTag.NUM_RESOURCES_SPENT_THIS_GAME)
                };

                // Only add if we don't already have data for this turn
                if (!_turns.Any(t => t.Turn == currentTurn))
                {
                    _turns.Add(turnData);
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info(
                        $"Added turn data for turn {currentTurn}: " +
                        $"Minions={turnData.NumMinionsPlayedThisTurn}, " +
                        $"Spells={turnData.NumSpellsPlayedThisGame}, " +
                        $"Resources={turnData.NumResourcesSpentThisGame}"
                    );
                }
                else
                {
                    // Update existing turn data
                    var existingTurn = _turns.First(t => t.Turn == currentTurn);
                    existingTurn.NumMinionsPlayedThisTurn = turnData.NumMinionsPlayedThisTurn;
                    existingTurn.NumSpellsPlayedThisGame = turnData.NumSpellsPlayedThisGame;
                    existingTurn.NumResourcesSpentThisGame = turnData.NumResourcesSpentThisGame;
                }

                _triplesCreated = playerEntity.GetTag(GameTag.PLAYER_TRIPLES);
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"OnTurnStart - Player Entity found, Triples: {_triplesCreated}, Tags: {string.Join(", ", playerEntity.Tags.Select(t => $"{t.Key}={t.Value}"))}");
            }
            else
            {
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("OnTurnStart - Player Entity not found");
            }

            // Check combat phase
            var gameEntity = Core.Game.GameEntity;
            if (gameEntity != null)
            {
                var currentStep = gameEntity.GetTag(GameTag.STEP);
                var nextStep = gameEntity.GetTag(GameTag.NEXT_STEP);
                var turnNumber = Core.Game.GetTurnNumber();
                var playState = gameEntity.GetTag(GameTag.PLAYSTATE);
                var zone = gameEntity.GetTag(GameTag.ZONE);

                var wasInCombat = _isInCombat;
                
                // Combat detection logic:
                // 1. Check if we're in the main combat step (STEP_MAIN_COMBAT = 4)
                // 2. Validate that we have a real opponent (not Bob)
                // 3. Check if there are any minions in play
                var potentialOpponent = Core.Game.Entities.Values
                    .FirstOrDefault(e => e.IsHero && e.IsInPlay && !e.IsPlayer && e.CardId != "TB_BaconShopBob");
                
                var hasMinionsInPlay = Core.Game.Player.Board.Any(e => e.IsMinion && e.IsInPlay) ||
                                      (potentialOpponent != null && Core.Game.Opponent.Board.Any(e => e.IsMinion && e.IsInPlay));

                _isInCombat = currentStep == 4 && potentialOpponent != null && hasMinionsInPlay;

                // Log detailed phase information
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"=== Phase Information ===");
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Current Step: {currentStep}");
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Next Step: {nextStep}");
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Turn Number: {turnNumber}");
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Play State: {playState}");
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Zone: {zone}");
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Has Minions In Play: {hasMinionsInPlay}");
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Has Real Opponent: {potentialOpponent != null}");
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Is Combat: {_isInCombat}");

                // If we just entered combat, reset damage counter and find opponent
                if (!wasInCombat && _isInCombat)
                {
                    _lastCombatDamageDealt = 0;
                    _lastCombatOpponent = potentialOpponent;
                    
                    if (_lastCombatOpponent != null)
                    {
                        Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Combat started against: {_lastCombatOpponent.Card?.LocalizedName ?? "Unknown"} ({_lastCombatOpponent.CardId})");
                    }
                }
                // If we just exited combat, log the damage dealt
                else if (wasInCombat && !_isInCombat && _lastCombatOpponent != null)
                {
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Combat ended. Damage dealt this combat: {_lastCombatDamageDealt}");
                    _totalDamageDealt += _lastCombatDamageDealt;
                }
            }
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
                _totalDamageDealt = 0;
                _triplesCreated = 0;
                _currentHeroId = null;
                _currentHeroName = null;
                gameStartTime = DateTime.Now;
                
                // Try to get hero immediately
                var hero = Core.Game.Player.Hero;
                if (hero != null)
                {
                    _currentHeroId = hero.CardId;
                    _currentHeroName = hero.Card?.Name;
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"OnGameStart - Found hero: {_currentHeroName} ({_currentHeroId})");
                }
                
                ShowGameStartOverlay();
            }
        }

        private void InitViewPanel()
        {
            stackPanel = new PlugInDisplayControl();
            Core.OverlayCanvas.Children.Add(stackPanel);

            // Initialize overlays
            gameStartOverlay = new TextBlock
            {
                Visibility = System.Windows.Visibility.Collapsed,
                Foreground = Brushes.White,
                FontSize = 14,
                Margin = new Thickness(10)
            };

            gameEndOverlay = new TextBlock
            {
                Visibility = System.Windows.Visibility.Collapsed,
                Foreground = Brushes.White,
                FontSize = 14,
                Margin = new Thickness(10)
            };

            // Add overlays directly to the stackPanel (since it is a StackPanel)
            stackPanel.Children.Add(gameStartOverlay);
            stackPanel.Children.Add(gameEndOverlay);

            // Initialize input manager
            inputMoveManager = new InputMoveManager(stackPanel);
        }

        private void ShowGameStartOverlay()
        {
            if (gameEndOverlay != null)
                gameEndOverlay.Visibility = System.Windows.Visibility.Collapsed;

            if (gameStartOverlay != null)
            {
                var heroEntity = Core.Game.Player.Hero;
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"ShowGameStartOverlay - Hero from Core.Game: {heroEntity?.Card?.LocalizedName ?? "null"} ({heroEntity?.CardId ?? "null"})");
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"ShowGameStartOverlay - Tracked Hero: {_currentHeroName ?? "null"} ({_currentHeroId ?? "null"})");
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"ShowGameStartOverlay - Tracked Anomaly: {_currentAnomalyName ?? "null"} ({_currentAnomalyId ?? "null"})");

                var heroName = _currentHeroName ?? heroEntity?.Card?.LocalizedName ?? "Unknown Hero";
                var health = heroEntity?.Health ?? 0;
                var anomalyText = _currentAnomalyName != null ? $"\nAnomaly: {_currentAnomalyName}" : "";

                gameStartOverlay.Text = $"Hero: {heroName}\nHealth: {health}{anomalyText}";
                gameStartOverlay.Visibility = System.Windows.Visibility.Visible;
            }
        }

        private void OnEntityWillTakeDamage(PredamageInfo predamageInfo)
        {
            if (!Core.Game.IsBattlegroundsMatch || !_isInCombat)
                return;

            // Only track damage to the opponent's hero during combat
            if (_lastCombatOpponent != null && 
                predamageInfo.Entity.Id == _lastCombatOpponent.Id)
            {
                _lastCombatDamageDealt += predamageInfo.Value;
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Damage dealt to opponent: {predamageInfo.Value} (Total this combat: {_lastCombatDamageDealt})");
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
                            logLines = reader.ReadToEnd().Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                        }

                        var currentTurn = 0;

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
                            else if (line.Contains("WinRate=") && line.Contains("TieRate=") && line.Contains("LossRate="))
                            {
                                var match = SimulationResultRegex.Match(line);
                                if (match.Success)
                                {
                                    // Get or create turn data
                                    var turnData = _turns.FirstOrDefault(t => t.Turn == currentTurn);
                                    if (turnData == null)
                                    {
                                        turnData = new TurnData { Turn = currentTurn };
                                        _turns.Add(turnData);
                                    }

                                    // Update simulation results
                                    turnData.WinRate = double.Parse(match.Groups[1].Value);
                                    turnData.TheirDeathRate = double.Parse(match.Groups[2].Value);
                                    turnData.TieRate = double.Parse(match.Groups[3].Value);
                                    turnData.LossRate = double.Parse(match.Groups[4].Value);
                                    turnData.MyDeathRate = double.Parse(match.Groups[5].Value);

                                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info(
                                        $"Updated turn {currentTurn} with simulation results: " +
                                        $"Win={turnData.WinRate}% (Lethal={turnData.TheirDeathRate}%), " +
                                        $"Tie={turnData.TieRate}%, " +
                                        $"Loss={turnData.LossRate}% (Lethal={turnData.MyDeathRate}%)"
                                    );

                                    // Update UI with latest simulation result
                                    if (stackPanel != null)
                                    {
                                        stackPanel.UpdateSimulationDisplay(turnData);
                                    }
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
                ParseHDTLog();

                var stats = Core.Game.CurrentGameStats;
                if (stats == null)
                {
                    Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("OnGameEnd: CurrentGameStats is null");
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
                        $"{(minion.Enchantments.Any() ? $" [Enchantments: {string.Join(", ", minion.Enchantments)}]" : "")}"
                    );
                }

                var gameData = new BattlegroundsGameData
                {
                    PlayerIdentifier = Core.Game.Player.Name,
                    Placement = placement,
                    StartingMmr = _startingMmr,
                    FinalMmr = stats.BattlegroundsRating,
                    MmrGained = stats.BattlegroundsRating - _startingMmr,
                    GameDurationInSeconds = (int)(DateTime.Now - gameStartTime).TotalSeconds,
                    GameEndDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ff"),
                    HeroPlayed = _currentHeroId ?? "Unknown",
                    HeroPlayedName = _currentHeroName ?? "Unknown",
                    AnomalyId = _currentAnomalyId ?? "None",
                    AnomalyName = _currentAnomalyName ?? "None",
                    TriplesCreated = triplesCreated,
                    FinalBoard = finalBoard,
                    TotalDamageDealt = _totalDamageDealt,
                    Turns = _turns
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

                // Update overlay if available
                if (gameStartOverlay != null)
                    gameStartOverlay.Visibility = System.Windows.Visibility.Collapsed;
                
                if (gameEndOverlay != null)
                {
                    gameEndOverlay.Text = $"Game Summary:\nPlacement: {gameData.Placement}\nMMR Change: {gameData.MmrGained}\nTriples: {gameData.TriplesCreated}";
                    gameEndOverlay.Visibility = System.Windows.Visibility.Visible;
                }

                // Clear simulation results for next game
                _turns.Clear();
            }
            catch (Exception ex)
            {
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Error($"Failed to save BG game data: {ex.Message}");
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        private void SettingsChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            stackPanel.RenderTransform = new ScaleTransform(Settings.Default.Scale / 100, Settings.Default.Scale / 100);
            stackPanel.Opacity = Settings.Default.Opacity / 100;
        }

        public void CleanUp()
        {
            if (stackPanel != null)
            {
                Core.OverlayCanvas.Children.Remove(stackPanel);
                Dispose();
            }
        }

        public void Dispose()
        {
            inputMoveManager.Dispose();
        }

        // Add method to update attacking entities
        public void UpdateAttackingEntities(Entity attacker, Entity defender)
        {
            if (!attacker.IsHero || !defender.IsHero)
                return;
            Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Updating entities with attacker={attacker.Card.Name}, defender={defender.Card.Name}");
            _defendingHero = defender;
            _attackingHero = attacker;
        }

        // Add method to handle new attacking entity
        public void HandleNewAttackingEntity(Entity newAttacker)
        {
            if (newAttacker.IsHero)
            {
                _lastAttackingHero = newAttacker;
                _lastAttackingHeroAttack = newAttacker.Attack;
            }
        }
    }
}