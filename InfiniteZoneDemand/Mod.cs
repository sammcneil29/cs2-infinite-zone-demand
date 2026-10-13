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

        /// <summary>Live settings instance, read by the Force* systems every update.</summary>
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

            // Each system runs in the same 16-frame slot as the game system that reads the values it forces
            // (it inherits that system's update offset), immediately before it. See ForceDemandSystemBase.
            updateSystem.UpdateBefore<ForceBuildingDemandSystem, ZoneSpawnSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<ForceHouseholdDemandSystem, HouseholdSpawnSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<ForceCommercialCompanyDemandSystem, CommercialSpawnSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateBefore<ForceIndustrialCompanyDemandSystem, IndustrialSpawnSystem>(SystemUpdatePhase.GameSimulation);
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
