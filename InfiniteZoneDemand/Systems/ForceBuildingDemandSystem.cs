using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Simulation;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace InfiniteZoneDemand.Systems
{
    /// <summary>
    /// Holds zone building demand at maximum, running immediately before <see cref="ZoneSpawnSystem"/>.
    ///
    /// How vanilla building demand flows (every 16 simulation frames):
    ///  - Commercial/Industrial/ResidentialDemandSystem.OnUpdate copy their private NativeValue results into
    ///    m_Last* fields (read by the public properties, the demand bars and the spawn gates), then schedule a
    ///    job that recomputes the NativeValues and the per-resource building-demand arrays.
    ///  - ZoneSpawnSystem gates each zone type on the public properties, then scores each candidate building by
    ///    summing the per-resource arrays for the products it sells/makes/stores. A building is skipped when
    ///    that sum is 0, so forcing only the headline numbers fills the bars but does not grow
    ///    commercial/industrial/office buildings.
    ///
    /// This system completes the demand jobs, then writes maximum values into the NativeValues (so the next copy
    /// into m_Last* keeps the bars full), into m_Last* (so this frame's spawn gates open) and into the
    /// per-resource arrays (so buildings are chosen).
    /// </summary>
    public partial class ForceBuildingDemandSystem : ForceDemandSystemBase
    {
        // Building demand is clamped to 0..100 by the game; the demand bars show value / 100.
        private const int kMaxBuildingDemand = 100;
        // What vanilla writes for a manufactured/office product that needs more buildings.
        private const int kIndustrialResourceDemand = 50;
        // What vanilla writes for a storable product that needs more warehouse space.
        private const int kStorageResourceDemand = 1;
        // Diagnostics are logged every this many updates (16 frames each).
        private const int kDiagnosticsInterval = 64;

        private ResidentialDemandSystem m_ResidentialDemandSystem;
        private CommercialDemandSystem m_CommercialDemandSystem;
        private IndustrialDemandSystem m_IndustrialDemandSystem;
        private ZoneSpawnSystem m_ZoneSpawnSystem;
        private CountResidentialPropertySystem m_CountResidentialPropertySystem;
        private Game.Prefabs.ResourceSystem m_ResourceSystem;
        private readonly ResourceGroups m_Resources = new ResourceGroups();

        // ResidentialDemandSystem
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

        private bool m_ResidentialFailed;
        private bool m_CommercialFailed;
        private bool m_IndustrialFailed;
        private bool m_FastSpawnSetByUs;
        private int m_UpdateCount;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ResidentialDemandSystem = World.GetOrCreateSystemManaged<ResidentialDemandSystem>();
            m_CommercialDemandSystem = World.GetOrCreateSystemManaged<CommercialDemandSystem>();
            m_IndustrialDemandSystem = World.GetOrCreateSystemManaged<IndustrialDemandSystem>();
            m_ZoneSpawnSystem = World.GetOrCreateSystemManaged<ZoneSpawnSystem>();
            m_CountResidentialPropertySystem = World.GetOrCreateSystemManaged<CountResidentialPropertySystem>();
            m_ResourceSystem = World.GetOrCreateSystemManaged<Game.Prefabs.ResourceSystem>();

            var missing = new List<string>();

            Type res = typeof(ResidentialDemandSystem);
            m_ResBuildingDemand = GameFields.Find(res, "m_BuildingDemand", missing);
            m_ResLastBuildingDemand = GameFields.Find(res, "m_LastBuildingDemand", missing);
            m_ResReadDependencies = GameFields.Find(res, "m_ReadDependencies", missing);

            Type com = typeof(CommercialDemandSystem);
            m_ComBuildingDemand = GameFields.Find(com, "m_BuildingDemand", missing);
            m_ComLastBuildingDemand = GameFields.Find(com, "m_LastBuildingDemand", missing);
            m_ComReadDependencies = GameFields.Find(com, "m_ReadDependencies", missing);

            Type ind = typeof(IndustrialDemandSystem);
            m_IndBuildingDemand = GameFields.Find(ind, "m_IndustrialBuildingDemand", missing);
            m_IndLastBuildingDemand = GameFields.Find(ind, "m_LastIndustrialBuildingDemand", missing);
            m_OffBuildingDemand = GameFields.Find(ind, "m_OfficeBuildingDemand", missing);
            m_OffLastBuildingDemand = GameFields.Find(ind, "m_LastOfficeBuildingDemand", missing);
            m_StoBuildingDemand = GameFields.Find(ind, "m_StorageBuildingDemand", missing);
            m_StoLastBuildingDemand = GameFields.Find(ind, "m_LastStorageBuildingDemand", missing);
            m_IndReadDependencies = GameFields.Find(ind, "m_ReadDependencies", missing);

            GameFields.ReportMissing(nameof(ForceBuildingDemandSystem), missing);
            if (missing.Count == 0)
            {
                Mod.log.Info("All demand-system fields found.");
            }
        }

        protected override void Force(Setting s)
        {
            if (!m_ResidentialFailed && (s.ResidentialLow || s.ResidentialMedium || s.ResidentialHigh))
            {
                Guard(ForceResidential, s, ref m_ResidentialFailed, "residential");
            }

            if (!m_CommercialFailed && s.Commercial)
            {
                Guard(ForceCommercial, s, ref m_CommercialFailed, "commercial");
            }

            if (!m_IndustrialFailed && (s.Industrial || s.Office || s.Warehouses))
            {
                Guard(ForceIndustrial, s, ref m_IndustrialFailed, "industrial/office/warehouse");
            }

            ForceFastSpawn(s);

            if (s.LogDiagnostics && ++m_UpdateCount % kDiagnosticsInterval == 0)
            {
                LogDiagnostics();
            }
        }

        private void ForceResidential(Setting s)
        {
            // GetLowDensityDemandFactors hands out the demand job's write handle; completing it guarantees the
            // job scheduled earlier this cycle has finished writing before we overwrite its results.
            m_ResidentialDemandSystem.GetLowDensityDemandFactors(out JobHandle writeDeps);
            writeDeps.Complete();
            GameFields.Complete(m_ResReadDependencies, m_ResidentialDemandSystem);

            if (m_ResBuildingDemand == null)
            {
                return;
            }

            int3 demand = GameFields.GetNative<int3>(m_ResBuildingDemand, m_ResidentialDemandSystem);
            if (s.ResidentialLow) demand.x = kMaxBuildingDemand;
            if (s.ResidentialMedium) demand.y = kMaxBuildingDemand;
            if (s.ResidentialHigh) demand.z = kMaxBuildingDemand;
            GameFields.SetNative(m_ResBuildingDemand, m_ResidentialDemandSystem, demand);
            m_ResLastBuildingDemand?.SetValue(m_ResidentialDemandSystem, demand);
        }

        private void ForceCommercial(Setting s)
        {
            NativeArray<int> buildingDemands = m_CommercialDemandSystem.GetBuildingDemands(out JobHandle writeDeps);
            writeDeps.Complete();
            GameFields.Complete(m_ComReadDependencies, m_CommercialDemandSystem);

            if (m_ComBuildingDemand != null)
            {
                GameFields.SetNative(m_ComBuildingDemand, m_CommercialDemandSystem, kMaxBuildingDemand);
                m_ComLastBuildingDemand?.SetValue(m_CommercialDemandSystem, kMaxBuildingDemand);
            }

            // The commercial job clears every entry at the start of each cycle, so raising all of them is
            // undone automatically. ZoneSpawnSystem only sums the entries a shop can actually sell.
            GameFields.RaiseAll(buildingDemands, kMaxBuildingDemand);
        }

        private void ForceIndustrial(Setting s)
        {
            NativeArray<int> buildingDemands = m_IndustrialDemandSystem.GetBuildingDemands(out JobHandle writeDeps);
            NativeArray<int> storageDemands = m_IndustrialDemandSystem.GetStorageBuildingDemands(out JobHandle storageDeps);
            JobHandle.CombineDependencies(writeDeps, storageDeps).Complete();
            GameFields.Complete(m_IndReadDependencies, m_IndustrialDemandSystem);

            if (s.Industrial && m_IndBuildingDemand != null)
            {
                GameFields.SetNative(m_IndBuildingDemand, m_IndustrialDemandSystem, kMaxBuildingDemand);
                m_IndLastBuildingDemand?.SetValue(m_IndustrialDemandSystem, kMaxBuildingDemand);
            }

            if (s.Office && m_OffBuildingDemand != null)
            {
                GameFields.SetNative(m_OffBuildingDemand, m_IndustrialDemandSystem, kMaxBuildingDemand);
                m_OffLastBuildingDemand?.SetValue(m_IndustrialDemandSystem, kMaxBuildingDemand);
            }

            if (s.Warehouses && m_StoBuildingDemand != null)
            {
                // Vanilla storage demand is a small unclamped count; the industrial bar shows
                // max(industrial, storage), so stay within the bar's 0..100 range.
                int demand = math.clamp(GameFields.GetNative<int>(m_StoBuildingDemand, m_IndustrialDemandSystem), 1, kMaxBuildingDemand);
                GameFields.SetNative(m_StoBuildingDemand, m_IndustrialDemandSystem, demand);
                m_StoLastBuildingDemand?.SetValue(m_IndustrialDemandSystem, demand);
            }

            if (!m_Resources.EnsureReady(EntityManager, m_ResourceSystem))
            {
                return; // Resource prefabs not loaded yet; try again next cycle.
            }

            if (s.Industrial)
            {
                GameFields.Raise(buildingDemands, m_Resources.Industrial, kIndustrialResourceDemand);
            }

            if (s.Office)
            {
                GameFields.Raise(buildingDemands, m_Resources.Office, kIndustrialResourceDemand);
            }

            if (s.Warehouses)
            {
                GameFields.Raise(storageDemands, m_Resources.Storable, kStorageResourceDemand);
            }
        }

        private void ForceFastSpawn(Setting s)
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

        private void LogDiagnostics()
        {
            int3 res = m_ResidentialDemandSystem.buildingDemand;
            CountResidentialPropertySystem.ResidentialPropertyData homes = m_CountResidentialPropertySystem.GetResidentialPropertyData();
            Mod.log.Info(
                $"Demand bars: residential {res.x}/{res.y}/{res.z}, commercial {m_CommercialDemandSystem.buildingDemand}, " +
                $"industrial {m_IndustrialDemandSystem.industrialBuildingDemand}, office {m_IndustrialDemandSystem.officeBuildingDemand}, " +
                $"storage {m_IndustrialDemandSystem.storageBuildingDemand} | household demand {m_ResidentialDemandSystem.householdDemand}, " +
                $"free homes {homes.m_FreeProperties.x}/{homes.m_FreeProperties.y}/{homes.m_FreeProperties.z} " +
                $"of {homes.m_TotalProperties.x}/{homes.m_TotalProperties.y}/{homes.m_TotalProperties.z} | " +
                $"company demand: commercial {m_CommercialDemandSystem.companyDemand}, industrial {m_IndustrialDemandSystem.industrialCompanyDemand}, " +
                $"office {m_IndustrialDemandSystem.officeCompanyDemand}");
        }

        private static void Guard(Action<Setting> force, Setting settings, ref bool failed, string what)
        {
            try
            {
                force(settings);
            }
            catch (Exception e)
            {
                failed = true;
                Mod.log.Error($"Forcing {what} building demand failed and has been disabled until the game restarts: {e}");
            }
        }
    }
}
