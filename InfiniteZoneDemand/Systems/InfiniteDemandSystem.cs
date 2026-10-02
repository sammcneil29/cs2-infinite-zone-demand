using System;
using System.Collections.Generic;
using System.Reflection;
using Colossal.Collections;
using Game;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace InfiniteZoneDemand.Systems
{
    /// <summary>
    /// Holds zone demand at maximum by overwriting the vanilla demand systems' results just before
    /// <see cref="ZoneSpawnSystem"/> reads them.
    ///
    /// How vanilla demand flows (every 16 simulation frames):
    ///  - Commercial/Industrial/ResidentialDemandSystem.OnUpdate copy their private NativeValue results into
    ///    m_Last* fields (read by the public properties, the demand bars and the spawn gates), then schedule a
    ///    job that recomputes the NativeValues and the per-resource building-demand arrays.
    ///  - ZoneSpawnSystem gates each zone type on the public properties, then scores each candidate building by
    ///    summing the per-resource arrays for the products it sells/makes/stores. A building is skipped when
    ///    that sum is 0, so forcing only the headline numbers fills the bars but does not grow
    ///    commercial/industrial/office buildings.
    ///
    /// This system runs in ZoneSpawnSystem's slot, immediately before it. It completes the demand jobs, then
    /// writes maximum values into the NativeValues (so the next copy into m_Last* keeps the bars full), into
    /// m_Last* (so this frame's spawn gates open) and into the per-resource arrays (so buildings are chosen).
    /// The demand jobs recompute everything from scratch next cycle, so turning an option off restores
    /// vanilla behaviour within one cycle.
    ///
    /// Private fields are accessed by reflection; a missing field is logged once and only that part is skipped.
    /// </summary>
    public partial class InfiniteDemandSystem : GameSystemBase
    {
        // Building demand is clamped to 0..100 by the game; the demand bars show value / 100.
        private const int kMaxBuildingDemand = 100;
        // Household demand is capped at 200 by the game and scales how many households move in per update.
        private const int kHouseholdDemand = 100;
        // What vanilla writes for a manufactured/office product that needs more buildings.
        private const int kIndustrialResourceDemand = 50;
        // What vanilla writes for a storable product that needs more warehouse space.
        private const int kStorageResourceDemand = 1;

        private const BindingFlags kFieldFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private ResidentialDemandSystem m_ResidentialDemandSystem;
        private CommercialDemandSystem m_CommercialDemandSystem;
        private IndustrialDemandSystem m_IndustrialDemandSystem;
        private ZoneSpawnSystem m_ZoneSpawnSystem;
        private Game.Prefabs.ResourceSystem m_ResourceSystem;

        // ResidentialDemandSystem
        private FieldInfo m_ResHouseholdDemand;      // NativeValue<int>
        private FieldInfo m_ResLastHouseholdDemand;  // int
        private FieldInfo m_ResBuildingDemand;       // NativeValue<int3> (x low, y medium, z high)
        private FieldInfo m_ResLastBuildingDemand;   // int3
        private FieldInfo m_ResReadDependencies;     // JobHandle

        // CommercialDemandSystem
        private FieldInfo m_ComBuildingDemand;       // NativeValue<int>
        private FieldInfo m_ComLastBuildingDemand;   // int
        private FieldInfo m_ComReadDependencies;     // JobHandle

        // IndustrialDemandSystem
        private FieldInfo m_IndBuildingDemand;       // NativeValue<int>
        private FieldInfo m_IndLastBuildingDemand;   // int
        private FieldInfo m_OffBuildingDemand;       // NativeValue<int>
        private FieldInfo m_OffLastBuildingDemand;   // int
        private FieldInfo m_StoBuildingDemand;       // NativeValue<int>
        private FieldInfo m_StoLastBuildingDemand;   // int
        private FieldInfo m_IndReadDependencies;     // JobHandle

        // Resource indexes the industrial job rewrites every cycle, split by what they are built for.
        // Only these are raised, so turning an option off is undone by the next vanilla recompute.
        private readonly List<int> m_IndustrialResources = new List<int>();
        private readonly List<int> m_OfficeResources = new List<int>();
        private readonly List<int> m_StorageResources = new List<int>();
        private bool m_ResourcesReady;

        private bool m_ResidentialFailed;
        private bool m_CommercialFailed;
        private bool m_IndustrialFailed;
        private bool m_FastSpawnFailed;
        private bool m_FastSpawnSetByUs;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            // Same interval as ZoneSpawnSystem and the demand systems. With no offset of our own, UpdateBefore
            // makes this system inherit ZoneSpawnSystem's offset, so it runs in the same frame, just before it.
            return 16;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ResidentialDemandSystem = World.GetOrCreateSystemManaged<ResidentialDemandSystem>();
            m_CommercialDemandSystem = World.GetOrCreateSystemManaged<CommercialDemandSystem>();
            m_IndustrialDemandSystem = World.GetOrCreateSystemManaged<IndustrialDemandSystem>();
            m_ZoneSpawnSystem = World.GetOrCreateSystemManaged<ZoneSpawnSystem>();
            m_ResourceSystem = World.GetOrCreateSystemManaged<Game.Prefabs.ResourceSystem>();

            var missing = new List<string>();

            Type res = typeof(ResidentialDemandSystem);
            m_ResHouseholdDemand = Field(res, "m_HouseholdDemand", missing);
            m_ResLastHouseholdDemand = Field(res, "m_LastHouseholdDemand", missing);
            m_ResBuildingDemand = Field(res, "m_BuildingDemand", missing);
            m_ResLastBuildingDemand = Field(res, "m_LastBuildingDemand", missing);
            m_ResReadDependencies = Field(res, "m_ReadDependencies", missing);

            Type com = typeof(CommercialDemandSystem);
            m_ComBuildingDemand = Field(com, "m_BuildingDemand", missing);
            m_ComLastBuildingDemand = Field(com, "m_LastBuildingDemand", missing);
            m_ComReadDependencies = Field(com, "m_ReadDependencies", missing);

            Type ind = typeof(IndustrialDemandSystem);
            m_IndBuildingDemand = Field(ind, "m_IndustrialBuildingDemand", missing);
            m_IndLastBuildingDemand = Field(ind, "m_LastIndustrialBuildingDemand", missing);
            m_OffBuildingDemand = Field(ind, "m_OfficeBuildingDemand", missing);
            m_OffLastBuildingDemand = Field(ind, "m_LastOfficeBuildingDemand", missing);
            m_StoBuildingDemand = Field(ind, "m_StorageBuildingDemand", missing);
            m_StoLastBuildingDemand = Field(ind, "m_LastStorageBuildingDemand", missing);
            m_IndReadDependencies = Field(ind, "m_ReadDependencies", missing);

            if (missing.Count > 0)
            {
                Mod.log.Warn($"Game fields not found (a game update probably renamed them); the related demand will not be forced: {string.Join(", ", missing)}");
            }
            else
            {
                Mod.log.Info("All demand-system fields found.");
            }
        }

        protected override void OnUpdate()
        {
            Setting s = Mod.Settings;
            if (s == null)
            {
                return;
            }

            if (!m_ResidentialFailed && (s.ResidentialLow || s.ResidentialMedium || s.ResidentialHigh || s.Households))
            {
                Guard(UpdateResidential, s, ref m_ResidentialFailed, "residential");
            }

            if (!m_CommercialFailed && s.Commercial)
            {
                Guard(UpdateCommercial, s, ref m_CommercialFailed, "commercial");
            }

            if (!m_IndustrialFailed && (s.Industrial || s.Office || s.Warehouses))
            {
                Guard(UpdateIndustrial, s, ref m_IndustrialFailed, "industrial/office/warehouse");
            }

            if (!m_FastSpawnFailed)
            {
                Guard(UpdateFastSpawn, s, ref m_FastSpawnFailed, "build anywhere");
            }
        }

        private void UpdateResidential(Setting s)
        {
            // GetLowDensityDemandFactors hands out the demand job's write handle; completing it guarantees the
            // job scheduled earlier this cycle has finished writing before we overwrite its results.
            m_ResidentialDemandSystem.GetLowDensityDemandFactors(out JobHandle writeDeps);
            writeDeps.Complete();
            CompleteHandle(m_ResReadDependencies, m_ResidentialDemandSystem);

            if (m_ResBuildingDemand != null && (s.ResidentialLow || s.ResidentialMedium || s.ResidentialHigh))
            {
                int3 demand = GetNativeValue<int3>(m_ResBuildingDemand, m_ResidentialDemandSystem);
                if (s.ResidentialLow) demand.x = kMaxBuildingDemand;
                if (s.ResidentialMedium) demand.y = kMaxBuildingDemand;
                if (s.ResidentialHigh) demand.z = kMaxBuildingDemand;
                SetNativeValue(m_ResBuildingDemand, m_ResidentialDemandSystem, demand);
                m_ResLastBuildingDemand?.SetValue(m_ResidentialDemandSystem, demand);
            }

            if (m_ResHouseholdDemand != null && s.Households)
            {
                int demand = math.max(GetNativeValue<int>(m_ResHouseholdDemand, m_ResidentialDemandSystem), kHouseholdDemand);
                SetNativeValue(m_ResHouseholdDemand, m_ResidentialDemandSystem, demand);
                m_ResLastHouseholdDemand?.SetValue(m_ResidentialDemandSystem, demand);
            }
        }

        private void UpdateCommercial(Setting s)
        {
            NativeArray<int> buildingDemands = m_CommercialDemandSystem.GetBuildingDemands(out JobHandle writeDeps);
            writeDeps.Complete();
            CompleteHandle(m_ComReadDependencies, m_CommercialDemandSystem);

            if (m_ComBuildingDemand != null)
            {
                SetNativeValue(m_ComBuildingDemand, m_CommercialDemandSystem, kMaxBuildingDemand);
                m_ComLastBuildingDemand?.SetValue(m_CommercialDemandSystem, kMaxBuildingDemand);
            }

            // The commercial job clears every entry at the start of each cycle, so raising all of them is
            // undone automatically. ZoneSpawnSystem only sums the entries a shop can actually sell.
            if (buildingDemands.IsCreated)
            {
                for (int i = 0; i < buildingDemands.Length; i++)
                {
                    buildingDemands[i] = math.max(buildingDemands[i], kMaxBuildingDemand);
                }
            }
        }

        private void UpdateIndustrial(Setting s)
        {
            NativeArray<int> buildingDemands = m_IndustrialDemandSystem.GetBuildingDemands(out JobHandle writeDeps);
            NativeArray<int> storageDemands = m_IndustrialDemandSystem.GetStorageBuildingDemands(out JobHandle storageDeps);
            JobHandle.CombineDependencies(writeDeps, storageDeps).Complete();
            CompleteHandle(m_IndReadDependencies, m_IndustrialDemandSystem);

            if (s.Industrial && m_IndBuildingDemand != null)
            {
                SetNativeValue(m_IndBuildingDemand, m_IndustrialDemandSystem, kMaxBuildingDemand);
                m_IndLastBuildingDemand?.SetValue(m_IndustrialDemandSystem, kMaxBuildingDemand);
            }

            if (s.Office && m_OffBuildingDemand != null)
            {
                SetNativeValue(m_OffBuildingDemand, m_IndustrialDemandSystem, kMaxBuildingDemand);
                m_OffLastBuildingDemand?.SetValue(m_IndustrialDemandSystem, kMaxBuildingDemand);
            }

            if (s.Warehouses && m_StoBuildingDemand != null)
            {
                // Vanilla storage demand is a small unclamped count; the industrial bar shows
                // max(industrial, storage), so stay within the bar's 0..100 range.
                int demand = math.clamp(GetNativeValue<int>(m_StoBuildingDemand, m_IndustrialDemandSystem), 1, kMaxBuildingDemand);
                SetNativeValue(m_StoBuildingDemand, m_IndustrialDemandSystem, demand);
                m_StoLastBuildingDemand?.SetValue(m_IndustrialDemandSystem, demand);
            }

            if (!m_ResourcesReady)
            {
                m_ResourcesReady = ClassifyResources();
                if (!m_ResourcesReady)
                {
                    return; // Resource prefabs not loaded yet; try again next cycle.
                }
            }

            if (buildingDemands.IsCreated)
            {
                if (s.Industrial)
                {
                    Raise(buildingDemands, m_IndustrialResources, kIndustrialResourceDemand);
                }

                if (s.Office)
                {
                    Raise(buildingDemands, m_OfficeResources, kIndustrialResourceDemand);
                }
            }

            if (s.Warehouses && storageDemands.IsCreated)
            {
                Raise(storageDemands, m_StorageResources, kStorageResourceDemand);
            }
        }

        private void UpdateFastSpawn(Setting s)
        {
            if (s.IgnoreLocation)
            {
                m_ZoneSpawnSystem.debugFastSpawn = true;
                m_FastSpawnSetByUs = true;
            }
            else if (m_FastSpawnSetByUs)
            {
                // Only switch it off if we switched it on, so the game's own debug toggle is left alone.
                m_ZoneSpawnSystem.debugFastSpawn = false;
                m_FastSpawnSetByUs = false;
            }
        }

        /// <summary>
        /// Sorts resources the same way IndustrialDemandSystem does: manufactured goods (producible, not raw
        /// materials) are industrial or office (weightless) products, and tradable physical goods can be stored.
        /// </summary>
        private bool ClassifyResources()
        {
            m_IndustrialResources.Clear();
            m_OfficeResources.Clear();
            m_StorageResources.Clear();

            ResourcePrefabs prefabs = m_ResourceSystem.GetPrefabs();
            ResourceIterator iterator = ResourceIterator.GetIterator();
            int found = 0;
            while (iterator.Next())
            {
                Entity prefab = prefabs[iterator.resource];
                if (prefab == Entity.Null || !EntityManager.HasComponent<ResourceData>(prefab))
                {
                    continue;
                }

                found++;
                ResourceData data = EntityManager.GetComponentData<ResourceData>(prefab);
                int index = EconomyUtils.GetResourceIndex(iterator.resource);
                bool weightless = data.m_Weight == 0f;

                if (data.m_IsTradable && !weightless)
                {
                    m_StorageResources.Add(index);
                }

                if (data.m_IsProduceable && !data.m_IsMaterial)
                {
                    (weightless ? m_OfficeResources : m_IndustrialResources).Add(index);
                }
            }

            if (found > 0)
            {
                Mod.log.Info($"Resources classified: {m_IndustrialResources.Count} industrial, {m_OfficeResources.Count} office, {m_StorageResources.Count} storable.");
            }

            return found > 0;
        }

        private static void Raise(NativeArray<int> demands, List<int> indexes, int value)
        {
            foreach (int i in indexes)
            {
                if (i >= 0 && i < demands.Length)
                {
                    demands[i] = math.max(demands[i], value);
                }
            }
        }

        private static FieldInfo Field(Type type, string name, List<string> missing)
        {
            FieldInfo field = type.GetField(name, kFieldFlags);
            if (field == null)
            {
                missing.Add($"{type.Name}.{name}");
            }
            return field;
        }

        private static void CompleteHandle(FieldInfo handleField, object system)
        {
            if (handleField?.GetValue(system) is JobHandle handle)
            {
                handle.Complete();
            }
        }

        private static T GetNativeValue<T>(FieldInfo field, object system) where T : unmanaged
        {
            return ((NativeValue<T>)field.GetValue(system)).value;
        }

        private static void SetNativeValue<T>(FieldInfo field, object system, T value) where T : unmanaged
        {
            // NativeValue is a handle to native memory, so writing through the copy updates the system's value;
            // storing the copy back as well keeps this correct even if that ever changes.
            var nativeValue = (NativeValue<T>)field.GetValue(system);
            nativeValue.value = value;
            field.SetValue(system, nativeValue);
        }

        private static void Guard(Action<Setting> update, Setting settings, ref bool failed, string what)
        {
            try
            {
                update(settings);
            }
            catch (Exception e)
            {
                // Stop retrying so a broken game API costs one log entry, not one per update.
                failed = true;
                Mod.log.Error($"Forcing {what} demand failed and has been disabled until the game restarts: {e}");
            }
        }
    }
}
