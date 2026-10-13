using System.Collections.Generic;
using System.Reflection;
using Game.Simulation;
using Unity.Mathematics;

namespace InfiniteZoneDemand.Systems
{
    /// <summary>
    /// Brings in households to fill empty homes, running immediately before <see cref="HouseholdSpawnSystem"/>.
    ///
    /// HouseholdSpawnSystem reads ResidentialDemandSystem.householdDemand (the m_LastHouseholdDemand cache) every
    /// 16 frames and spawns roughly householdDemand / (150 / ln(population)) new households from outside the city.
    /// The game caps that demand at 200, and it falls when homes stand empty, so forced building growth outpaces
    /// move-ins. This system raises it in proportion to the number of free homes: arrivals speed up while homes
    /// are empty and fall back to vanilla once they are full, so it does not create homeless households.
    /// </summary>
    public partial class ForceHouseholdDemandSystem : ForceDemandSystemBase
    {
        // Household demand added per free home, and the most it is raised to. Higher means faster move-ins.
        private const int kDemandPerFreeHome = 2;
        private const int kMaxHouseholdDemand = 2000;

        private ResidentialDemandSystem m_ResidentialDemandSystem;
        private CountResidentialPropertySystem m_CountResidentialPropertySystem;
        private FieldInfo m_LastHouseholdDemand; // int, read by HouseholdSpawnSystem via householdDemand

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ResidentialDemandSystem = World.GetOrCreateSystemManaged<ResidentialDemandSystem>();
            m_CountResidentialPropertySystem = World.GetOrCreateSystemManaged<CountResidentialPropertySystem>();

            var missing = new List<string>();
            m_LastHouseholdDemand = GameFields.Find(typeof(ResidentialDemandSystem), "m_LastHouseholdDemand", missing);
            GameFields.ReportMissing(nameof(ForceHouseholdDemandSystem), missing);
        }

        protected override void Force(Setting s)
        {
            if (!s.Households || m_LastHouseholdDemand == null)
            {
                return;
            }

            int3 free = m_CountResidentialPropertySystem.GetResidentialPropertyData().m_FreeProperties;
            int freeHomes = math.csum(math.max(free, int3.zero));
            if (freeHomes <= 0)
            {
                return; // Every home is taken: leave vanilla demand alone so nobody arrives to be homeless.
            }

            GameFields.RaiseInt(m_LastHouseholdDemand, m_ResidentialDemandSystem, math.min(freeHomes * kDemandPerFreeHome, kMaxHouseholdDemand));
        }
    }
}
