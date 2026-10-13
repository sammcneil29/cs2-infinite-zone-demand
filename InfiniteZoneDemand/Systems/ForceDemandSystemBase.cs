using System;
using Game;

namespace InfiniteZoneDemand.Systems
{
    /// <summary>
    /// Base for systems that overwrite a vanilla demand value just before one specific game system reads it.
    ///
    /// Each subclass is registered with UpdateBefore&lt;Self, Consumer&gt;. Every demand consumer runs every 16
    /// frames, and a system with the same interval and no offset of its own inherits the consumer's offset, so
    /// the subclass runs in the same frame, immediately before it. That matters because the demand jobs
    /// recompute everything on their own frames: a value forced on any other frame would be gone by the time
    /// the consumer reads it.
    /// </summary>
    public abstract partial class ForceDemandSystemBase : GameSystemBase
    {
        private bool m_Failed;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 16;
        }

        protected sealed override void OnUpdate()
        {
            Setting settings = Mod.Settings;
            if (m_Failed || settings == null)
            {
                return;
            }

            try
            {
                Force(settings);
            }
            catch (Exception e)
            {
                // Stop retrying so a broken game API costs one log entry, not one per update.
                m_Failed = true;
                Mod.log.Error($"{GetType().Name} failed and has been disabled until the game restarts: {e}");
            }
        }

        protected abstract void Force(Setting settings);
    }
}
