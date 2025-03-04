using Hearthstone_Deck_Tracker.Plugins;
using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace BattlegroundsGameCollection
{
    /// <summary>
    /// Wires up your plug-ins' logic once HDT loads it in to the session.
    /// </summary>
    /// <seealso cref="Hearthstone_Deck_Tracker.Plugins.IPlugin" />
    public class HDTBootstrap : IPlugin
    {
        /// <summary>
        /// The Plug-in's running instance
        /// </summary>
        public BattlegroundsGameCollection pluginInstance;

        /// <summary>
        /// The author, so your name.
        /// </summary>
        /// <value>The author's name.</value>
        public string Author => "LiiHS";

        public string ButtonText => "BG Game Collection";

        /// <summary>
        /// The Plug-in's description.
        /// </summary>
        /// <value>The Plug-in's description.</value>
        public string Description => "Collects Battlegrounds game data and logs it for analysis";

        /// <summary>
        /// Gets or sets the main <see cref="MenuItem">Menu Item</see>.
        /// </summary>
        /// <value>The main <see cref="MenuItem">Menu Item</see>.</value>
        public MenuItem MenuItem { get; set; } = null;

        public string Name => "BG Game Collection";

        /// <summary>
        /// The gets plug-in version.from the assembly
        /// </summary>
        /// <value>The plug-in assembly version.</value>
        public Version Version => Assembly.GetExecutingAssembly().GetName().Version;

        public void OnButtonPress() { }

        public void OnLoad()
        {
            // Create BGGames directory if it doesn't exist
            var bgGamesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BGGames");
            if (!Directory.Exists(bgGamesDir))
            {
                Directory.CreateDirectory(bgGamesDir);
            }

            pluginInstance = new BattlegroundsGameCollection();
            MenuItem = new MenuItem { Header = Name };
        }

        /// <summary>
        /// Called when during the window clean-up.
        /// </summary>
        public void OnUnload()
        {
            if (pluginInstance != null)
            {
                pluginInstance.Dispose();
                pluginInstance = null;
            }
        }

        /// <summary>
        /// Called when [update].
        /// </summary>
        public void OnUpdate() { }
    }
}