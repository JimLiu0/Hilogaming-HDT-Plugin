using Hearthstone_Deck_Tracker.Plugins;
using BattlegroundsGameCollection.Controls;
using BattlegroundsGameCollection.Properties;
using System;
using System.Reflection;
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
        public string Author => "Author Name";
// ToDo: put your name as the author


        public string ButtonText => LocalizeTools.GetLocalized("LabelSettings");

       // ToDo: Update the Plug-in Description in StringsResource.resx        
       public string Description => LocalizeTools.GetLocalized("TextDescription");

        /// <summary>
        /// Gets or sets the main <see cref="MenuItem">Menu Item</see>.
        /// </summary>
        /// <value>The main <see cref="MenuItem">Menu Item</see>.</value>
        public MenuItem MenuItem { get; set; } = null;

        public string Name => LocalizeTools.GetLocalized("TextName");

        /// <summary>
        /// The gets plug-in version.from the assembly
        /// </summary>
        /// <value>The plug-in assembly version.</value>
        public Version Version => Assembly.GetExecutingAssembly().GetName().Version;

        /// <summary>
        /// Adds the menu item.
        /// </summary>
        private void AddMenuItem()
        {
            this.MenuItem = new MenuItem()
            {
                Header = Name
            };

            this.MenuItem.Click += (sender, args) =>
            {
                OnButtonPress();
            };
        }

        public void OnButtonPress() => SettingsView.Flyout.IsOpen = true;

        public void OnLoad()
        {
            pluginInstance = new BattlegroundsGameCollection();
            AddMenuItem();
        }

        /// <summary>
        /// Called when during the window clean-up.
        /// </summary>
        public void OnUnload()
        {
            Settings.Default.Save();

            pluginInstance?.CleanUp();
            pluginInstance = null;
        }

        /// <summary>
        /// Called when [update].
        /// </summary>
        public void OnUpdate()
        { }
    }
}