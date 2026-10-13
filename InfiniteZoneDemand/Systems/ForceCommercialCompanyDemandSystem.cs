using System.Collections.Generic;
using System.Reflection;
using Game.Simulation;
using Unity.Collections;
using Unity.Jobs;

namespace InfiniteZoneDemand.Systems
{
    /// <summary>
    /// Brings in shops to fill empty commercial buildings, running immediately before
    /// <see cref="CommercialSpawnSystem"/>.
    ///
    /// Every 128 frames CommercialSpawnSystem creates one company per product whose per-product company demand
    /// (CommercialDemandSystem.GetResourceDemands) is above 0, provided no company selling that product is still
    /// waiting for a building and a per-product cooldown has passed. New companies then move into free buildings.
    /// Forcing building demand alone grows buildings for products nobody is queuing to sell, so they stand empty.
    /// Raising every product's company demand keeps one company per product on its way at all times; the
    /// "nobody already waiting" rule stops it once there are no free buildings left.
    /// </summary>
    public partial class ForceCommercialCompanyDemandSystem : ForceDemandSystemBase
    {
        // Any value above 0 lets a product spawn a company; 100 is the vanilla maximum.
        private const int kCompanyDemand = 100;

        private CommercialDemandSystem m_CommercialDemandSystem;
        private FieldInfo m_LastCompanyDemand;   // int, the companyDemand > 0 gate
        private FieldInfo m_ReadDependencies;    // JobHandle

        protected override void OnCreate()
        {
            base.OnCreate();

            m_CommercialDemandSystem = World.GetOrCreateSystemManaged<CommercialDemandSystem>();

            var missing = new List<string>();
            m_LastCompanyDemand = GameFields.Find(typeof(CommercialDemandSystem), "m_LastCompanyDemand", missing);
            m_ReadDependencies = GameFields.Find(typeof(CommercialDemandSystem), "m_ReadDependencies", missing);
            GameFields.ReportMissing(nameof(ForceCommercialCompanyDemandSystem), missing);
        }

        protected override void Force(Setting s)
        {
            if (!s.Commercial || !s.FillBusinesses)
            {
                return;
            }

            NativeArray<int> resourceDemands = m_CommercialDemandSystem.GetResourceDemands(out JobHandle writeDeps);
            writeDeps.Complete();
            GameFields.Complete(m_ReadDependencies, m_CommercialDemandSystem);

            // The commercial demand job clears every entry at the start of each cycle, so this is undone
            // automatically when the option is switched off. Products with no shop type are skipped by the spawner.
            GameFields.RaiseAll(resourceDemands, kCompanyDemand);
            GameFields.RaiseInt(m_LastCompanyDemand, m_CommercialDemandSystem, 1);
        }
    }
}
