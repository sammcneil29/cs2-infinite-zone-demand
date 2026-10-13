using System.Collections.Generic;
using System.Reflection;
using Game.Simulation;
using Unity.Collections;
using Unity.Jobs;

namespace InfiniteZoneDemand.Systems
{
    /// <summary>
    /// Brings in factories and offices to fill empty industrial and office buildings, running immediately before
    /// <see cref="IndustrialSpawnSystem"/>.
    ///
    /// Every 128 frames IndustrialSpawnSystem goes through the manufactured and office products and creates a
    /// company for one when a random roll, NextInt(5000 / log10(population)) with the divisor kept between 1 and 5,
    /// lands below that product's company demand (IndustrialDemandSystem.GetResourceDemands, at most 100 in
    /// vanilla) and no company making it is still waiting for a building. Vanilla odds are at best 1 in 10 per
    /// product, so forced buildings fill far more slowly than they grow. Raising the company demand to the
    /// roll's maximum makes every roll succeed; the "nobody already waiting" rule still stops it once there are no
    /// free buildings left. Raw-material extractors and warehouses are left to vanilla.
    /// </summary>
    public partial class ForceIndustrialCompanyDemandSystem : ForceDemandSystemBase
    {
        // The largest value IndustrialSpawnSystem rolls against, so the roll always succeeds.
        private const int kGuaranteedSpawnDemand = 5000;

        private IndustrialDemandSystem m_IndustrialDemandSystem;
        private Game.Prefabs.ResourceSystem m_ResourceSystem;
        private readonly ResourceGroups m_Resources = new ResourceGroups();
        private FieldInfo m_LastIndustrialCompanyDemand; // int, part of the spawner's company-demand gate
        private FieldInfo m_LastOfficeCompanyDemand;     // int, part of the spawner's company-demand gate
        private FieldInfo m_ReadDependencies;            // JobHandle

        protected override void OnCreate()
        {
            base.OnCreate();

            m_IndustrialDemandSystem = World.GetOrCreateSystemManaged<IndustrialDemandSystem>();
            m_ResourceSystem = World.GetOrCreateSystemManaged<Game.Prefabs.ResourceSystem>();

            var missing = new List<string>();
            m_LastIndustrialCompanyDemand = GameFields.Find(typeof(IndustrialDemandSystem), "m_LastIndustrialCompanyDemand", missing);
            m_LastOfficeCompanyDemand = GameFields.Find(typeof(IndustrialDemandSystem), "m_LastOfficeCompanyDemand", missing);
            m_ReadDependencies = GameFields.Find(typeof(IndustrialDemandSystem), "m_ReadDependencies", missing);
            GameFields.ReportMissing(nameof(ForceIndustrialCompanyDemandSystem), missing);
        }

        protected override void Force(Setting s)
        {
            bool industrial = s.Industrial && s.FillBusinesses;
            bool office = s.Office && s.FillBusinesses;
            if (!industrial && !office)
            {
                return;
            }

            if (!m_Resources.EnsureReady(EntityManager, m_ResourceSystem))
            {
                return; // Resource prefabs not loaded yet; try again next cycle.
            }

            // Despite the name, this is the per-product company demand array (m_IndustrialCompanyDemands).
            NativeArray<int> companyDemands = m_IndustrialDemandSystem.GetResourceDemands(out JobHandle writeDeps);
            writeDeps.Complete();
            GameFields.Complete(m_ReadDependencies, m_IndustrialDemandSystem);

            // The industrial demand job rewrites these entries every cycle, so this is undone automatically when
            // an option is switched off.
            if (industrial)
            {
                GameFields.Raise(companyDemands, m_Resources.Industrial, kGuaranteedSpawnDemand);
                GameFields.RaiseInt(m_LastIndustrialCompanyDemand, m_IndustrialDemandSystem, 1);
            }

            if (office)
            {
                GameFields.Raise(companyDemands, m_Resources.Office, kGuaranteedSpawnDemand);
                GameFields.RaiseInt(m_LastOfficeCompanyDemand, m_IndustrialDemandSystem, 1);
            }
        }
    }
}
