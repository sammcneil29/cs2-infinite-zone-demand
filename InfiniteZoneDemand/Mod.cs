using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using Game.Simulation;
using InfiniteZoneDemand.Systems;

namespace InfiniteZoneDemand
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(InfiniteZoneDemand)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        /// <summary>Live settings instance, read by <see cref="InfiniteDemandSystem"/> every update.</summary>
        public static Setting Settings { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            Settings = new Setting(this);
            Settings.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));
            AssetDatabase.global.LoadSettings(nameof(InfiniteZoneDemand), Settings, new Setting(this));

            // Runs in the same 16-frame slot as ZoneSpawnSystem (it inherits the spawner's update
            // offset because it has the same interval and no offset of its own), immediately before it.
            updateSystem.UpdateBefore<InfiniteDemandSystem, ZoneSpawnSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }
        }
    }
}
