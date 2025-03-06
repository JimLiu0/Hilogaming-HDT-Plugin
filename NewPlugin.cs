using Hearthstone_Deck_Tracker.API;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace BattlegroundsGameCollection
{
    public class GameData
    {
        public string playerIdentifier { get; set; }
        public string placement { get; set; }
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
        public string opponentId { get; set; }
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
        public Tag[] tags { get; set; }
    }

    public class Tag
    {
        public string ATK { get; set; }
        public string HEALTH { get; set; }
    }

    // Needed to calculate damage
    public class HealthData
    {
        public string playerId { get; set; }
        public int health { get; set; }
        public int armor { get; set; }
        public int damage { get; set; }
        public int totalHealth => health + armor - damage;
    }

    public class BattlegroundsGameCollection : IDisposable
    {
        private GameData game = new GameData();
        private DateTime gameStartTime;
        private HealthData[] startOfShopPhaseHealths = new HealthData[];
        private HealthData[] startOfCombatPhaseHealths = new HealthData[];

        public BattlegroundsGameCollection()
        {
            GameEvents.OnGameStart.Add(() => OnGameStart().GetAwaiter().GetResult());
            GameEvents.OnGameEnd.Add(OnGameEnd);
            GameEvents.OnTurnStart.Add(OnTurnStart);
            GameEvents.OnPlayerCreateInPlay.Add(OnPlayerCreateInPlay);
        }

        public void Dispose()
        {
            game = null;
        }

        private void onGameStart()
        {
            if (Core.Game.CurrentGameMode != GameMode.Battlegrounds) return;

            gameStartTime = DateTime.Now;
            game = new GameData();
            startOfShopPhaseHealths = new HealthData[];
            startOfCombatPhaseHealths = new HealthData[];
            switch (Core.Game.CurrentRegion)
            {
                case Region.US:
                    game.server = "REGION_US";
                case Region.EU:
                    game.server = "REGION_EU";
                default:
                    game.server = "REGION_AP";
            }
            // Figure out playerIdentifier
        }

        private void onTurnStart(ActivePlayer player)
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

                // Create new turn but don't have to fill it in
                var turnData = new TurnData();
                turnData.turn = currentTurn;
            }

            // Combat phase
            if (player == ActivePlayer.Opponent)
            {
                startOfCombatPhaseHealths = currentHealths;

                if (game.turns is not null && game.turns.Length > 0)
                {
                    var lastTurn = game.turns.last();

                    game.triplesCreated = mainPlayerEntity.GetTag(GameTag.PLAYER_TRIPLES) ?? 0;
                    lastTurn.numMinionsPlayedThisTurn = mainPlayerEntity.GetTag(GameTag.NUM_MINIONS_PLAYED_THIS_TURN) ?? 0;
                    lastTurn.numSpellsPlayedThisGame = mainPlayerEntity.GetTag(GameTag.NUM_SPELLS_PLAYED_THIS_GAME) ?? 0;
                    lastTurn.numResourcesSpentThisGame = mainPlayerEntity.GetTag(GameTag.NUM_RESOURCES_SPENT_THIS_GAME) ?? 0;
                    lastTurn.tavernTier = mainPlayerEntity.GetTag(GameTag.PLAYER_TECH_LEVEL) ?? 0;
                }
            }
        }

        private void OnGameEnd()
        {
            if (Core.Game.CurrentGameMode != GameMode.Battlegrounds)
                return;

            // Calculate final damage and get fight simulation data TBD
            ProcessFight();
      
            // Do placement stuff and meta data stuff
            game.placement = Core.Game.Entities.Values.FirstOrDefault(x => x.IsPlayer).GetTag(GameTag.PLAYER_LEADERBOARD_PLACE);
            CalculateAndUpdateMmr();
            game.gameEndDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ff");
            game.gameDurationInSeconds = (int)(DateTime.Now - gameStartTime).TotalSeconds;
            game.heroPlayed = Core.Game.Player.Hero.CardId;
            game.heroPlayedName = Core.Game.Player.Hero.Card?.LocalizedName;

            // Do final board stuff phase 2
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
        }

        private void ProcessFight()
        {
            if (game.turns is not null && game.turns.Length > 0)
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

            var lastTurn = game.turns.last();
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
