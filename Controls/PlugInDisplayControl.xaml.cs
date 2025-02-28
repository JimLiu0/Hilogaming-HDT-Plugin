using Hearthstone_Deck_Tracker.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Card = Hearthstone_Deck_Tracker.Hearthstone.Card;
using Core = Hearthstone_Deck_Tracker.API.Core;

namespace BattlegroundsGameCollection.Controls
{
    /// <summary>
    /// Interaction logic for PlugInDisplayControl.xaml
    /// </summary>
    public partial class PlugInDisplayControl : StackPanel
    {
        private TextBlock _simulationText;

        public PlugInDisplayControl()
        {
            InitializeComponent();
            InitializeSimulationDisplay();
        }

        private void InitializeSimulationDisplay()
        {
            _simulationText = new TextBlock
            {
                Visibility = Visibility.Collapsed,
                Foreground = Brushes.White,
                FontSize = 14,
                Margin = new Thickness(10),
                TextWrapping = TextWrapping.Wrap
            };

            Children.Add(_simulationText);
        }

        public void UpdateSimulationDisplay(TurnData turnData)
        {
            if (turnData == null)
            {
                _simulationText.Visibility = Visibility.Collapsed;
                return;
            }

            var text = $"Turn {turnData.Turn} Stats:\n" +
                       $"Win: {turnData.WinRate:F1}% (Lethal: {turnData.TheirDeathRate:F1}%)\n" +
                       $"Tie: {turnData.TieRate:F1}%\n" +
                       $"Loss: {turnData.LossRate:F1}% (Lethal: {turnData.MyDeathRate:F1}%)\n" +
                       $"Minions Played: {turnData.NumMinionsPlayedThisTurn}\n" +
                       $"Spells Played: {turnData.NumSpellsPlayedThisGame}\n" +
                       $"Resources Spent: {turnData.NumResourcesSpentThisGame}\n\n";

            // Add combat results if available
            if (turnData.CombatResult != null)
            {
                text += $"Last Combat vs {turnData.CombatResult.OpponentHeroName}:\n" +
                        $"Damage Dealt: {turnData.CombatResult.DamageDealt}\n" +
                        $"  Armor: {turnData.CombatResult.ArmorDamageDealt}\n" +
                        $"  Health: {turnData.CombatResult.HealthDamageDealt}\n" +
                        $"Damage Taken: {turnData.CombatResult.DamageTaken}\n" +
                        $"  Armor: {turnData.CombatResult.ArmorDamageTaken}\n" +
                        $"  Health: {turnData.CombatResult.HealthDamageTaken}\n" +
                        $"Result: {(turnData.CombatResult.Won ? "Won" : "Lost")}\n\n";
            }

            text += "Player Health:\n";
            foreach (var player in turnData.PlayerHealths.OrderBy(p => p.PlayerId))
            {
                text += $"{player.HeroName}: {player.Health}";
                if (player.Armor > 0)
                    text += $" (+{player.Armor} Armor)";
                text += "\n";
            }

            _simulationText.Text = text;
            _simulationText.Visibility = Visibility.Visible;
        }
    }
}