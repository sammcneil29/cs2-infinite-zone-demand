using System.Collections.Generic;
using Game.Economy;
using Game.Prefabs;
using Unity.Entities;

namespace InfiniteZoneDemand.Systems
{
    /// <summary>
    /// Sorts resources the same way IndustrialDemandSystem does: manufactured goods (producible, not raw
    /// materials) are industrial products, or office products when weightless; tradable physical goods can be
    /// stored in warehouses. The industrial demand job rewrites exactly these entries every cycle, so raising only
    /// them means a switched-off option is undone by the next vanilla recompute.
    /// </summary>
    internal sealed class ResourceGroups
    {
        public readonly List<int> Industrial = new List<int>();
        public readonly List<int> Office = new List<int>();
        public readonly List<int> Storable = new List<int>();

        public bool Ready { get; private set; }

        /// <summary>Classifies once resource prefabs are loaded; returns false (try again later) until then.</summary>
        public bool EnsureReady(EntityManager entityManager, Game.Prefabs.ResourceSystem resourceSystem)
        {
            if (Ready)
            {
                return true;
            }

            Industrial.Clear();
            Office.Clear();
            Storable.Clear();

            ResourcePrefabs prefabs = resourceSystem.GetPrefabs();
            ResourceIterator iterator = ResourceIterator.GetIterator();
            int found = 0;
            while (iterator.Next())
            {
                Entity prefab = prefabs[iterator.resource];
                if (prefab == Entity.Null || !entityManager.HasComponent<ResourceData>(prefab))
                {
                    continue;
                }

                found++;
                ResourceData data = entityManager.GetComponentData<ResourceData>(prefab);
                int index = EconomyUtils.GetResourceIndex(iterator.resource);
                bool weightless = data.m_Weight == 0f;

                if (data.m_IsTradable && !weightless)
                {
                    Storable.Add(index);
                }

                if (data.m_IsProduceable && !data.m_IsMaterial)
                {
                    (weightless ? Office : Industrial).Add(index);
                }
            }

            Ready = found > 0;
            if (Ready)
            {
                Mod.log.Info($"Resources classified: {Industrial.Count} industrial, {Office.Count} office, {Storable.Count} storable.");
            }
            return Ready;
        }
    }
}
