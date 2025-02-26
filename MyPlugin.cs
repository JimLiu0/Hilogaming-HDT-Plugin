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
using HearthMirror;
using Hearthstone_Deck_Tracker.Hearthstone;
using Hearthstone_Deck_Tracker.Stats;
using HearthDb.Enums;
using System.Linq;
using System.Windows;

namespace BattlegroundsGameCollection
{
    public class BattlegroundsGameData
    {
        public string PlayerIdentifier { get; set; }
        public int Placement { get; set; }
        public int StartingMmr { get; set; }
        public int MmrGained { get; set; }
        public int GameDurationInSeconds { get; set; }
        public string GameEndDate { get; set; }
        public string HeroPlayed { get; set; }
        public string HeroPlayedName { get; set; }
        public string AnomalyId { get; set; }
        public string AnomalyName { get; set; }
        public int TriplesCreated { get; set; }
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

            // Track triples created this turn
            var playerEntity = Core.Game.Entities.Values
                .FirstOrDefault(x => x.IsPlayer);
            if (playerEntity != null)
            {
                _triplesCreated = playerEntity.GetTag(GameTag.PLAYER_TRIPLES);
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"OnTurnStart - Player Entity found, Triples: {_triplesCreated}, Tags: {string.Join(", ", playerEntity.Tags.Select(t => $"{t.Key}={t.Value}"))}");
            }
            else
            {
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("OnTurnStart - Player Entity not found");
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
            if (Core.Game.CurrentGameMode != GameMode.Battlegrounds)
                return;

            // Track damage dealt by the player's entities
            if (predamageInfo.Entity.IsControlledBy(Core.Game.Player.Id))
            {
                _totalDamageDealt += predamageInfo.Value;
            }
        }

        private void OnGameEnd()
        {
            if (Core.Game.CurrentGameMode != GameMode.Battlegrounds)
                return;

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

            try
            {
                // Debug logging for all relevant data
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info("=== OnGameEnd Debug Information ===");
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Game Mode: {Core.Game.CurrentGameMode}");
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"Player Name: {Core.Game.Player.Name}");
                Hearthstone_Deck_Tracker.Utility.Logging.Log.Info($"MMR - Starting: {_startingMmr}, Current: {stats.BattlegroundsRating}, After: {stats.BattlegroundsRatingAfter}");
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

                var gameData = new BattlegroundsGameData
                {
                    PlayerIdentifier = Core.Game.Player.Name,
                    Placement = placement,
                    StartingMmr = _startingMmr,
                    MmrGained = stats.BattlegroundsRatingAfter - stats.BattlegroundsRating,
                    GameDurationInSeconds = (int)(DateTime.Now - gameStartTime).TotalSeconds,
                    GameEndDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ff"),
                    HeroPlayed = _currentHeroId ?? "Unknown",
                    HeroPlayedName = _currentHeroName ?? "Unknown",
                    AnomalyId = _currentAnomalyId ?? "None",
                    AnomalyName = _currentAnomalyName ?? "None",
                    TriplesCreated = triplesCreated
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
            }
            catch (Exception ex)
            {
                // Log any errors that occur
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
    }
}