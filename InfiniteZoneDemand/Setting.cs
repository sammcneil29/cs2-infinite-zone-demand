using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using System.Collections.Generic;

namespace InfiniteZoneDemand
{
    [FileLocation("ModsSettings/" + nameof(InfiniteZoneDemand) + "/" + nameof(InfiniteZoneDemand))]
    [SettingsUIGroupOrder(kResidentialGroup, kBusinessGroup, kAdvancedGroup)]
    [SettingsUIShowGroupName(kResidentialGroup, kBusinessGroup, kAdvancedGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";

        public const string kResidentialGroup = "Residential";
        public const string kBusinessGroup = "Business";
        public const string kAdvancedGroup = "Advanced";

        public Setting(IMod mod) : base(mod)
        {
            // The game never calls SetDefaults on mod settings, so apply them here. This also keeps the
            // live instance and the defaults instance passed to LoadSettings identical.
            SetDefaults();
        }

        [SettingsUISection(kSection, kResidentialGroup)]
        public bool ResidentialLow { get; set; }

        [SettingsUISection(kSection, kResidentialGroup)]
        public bool ResidentialMedium { get; set; }

        [SettingsUISection(kSection, kResidentialGroup)]
        public bool ResidentialHigh { get; set; }

        [SettingsUISection(kSection, kResidentialGroup)]
        public bool Households { get; set; }

        [SettingsUISection(kSection, kBusinessGroup)]
        public bool Commercial { get; set; }

        [SettingsUISection(kSection, kBusinessGroup)]
        public bool Industrial { get; set; }

        [SettingsUISection(kSection, kBusinessGroup)]
        public bool Office { get; set; }

        [SettingsUISection(kSection, kBusinessGroup)]
        public bool FillBusinesses { get; set; }

        [SettingsUISection(kSection, kBusinessGroup)]
        public bool Warehouses { get; set; }

        [SettingsUISection(kSection, kAdvancedGroup)]
        public bool IgnoreLocation { get; set; }

        [SettingsUISection(kSection, kAdvancedGroup)]
        public bool LogDiagnostics { get; set; }

        public override void SetDefaults()
        {
            ResidentialLow = true;
            ResidentialMedium = true;
            ResidentialHigh = true;
            Households = true;
            Commercial = true;
            Industrial = true;
            Office = true;
            FillBusinesses = true;
            Warehouses = false;
            IgnoreLocation = false;
            LogDiagnostics = false;
        }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocaleEN(Setting setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Infinite Zone Demand" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },

                { m_Setting.GetOptionGroupLocaleID(Setting.kResidentialGroup), "Residential" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kBusinessGroup), "Commercial, industrial and office" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kAdvancedGroup), "Advanced" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ResidentialLow)), "Low density housing" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ResidentialLow)), "Keep low density residential demand at maximum." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ResidentialMedium)), "Medium density housing" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ResidentialMedium)), "Keep medium density residential demand at maximum." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ResidentialHigh)), "High density housing" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ResidentialHigh)), "Keep high density residential demand at maximum." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Households)), "Fill empty homes" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Households)), "Bring in new households faster the more homes stand empty, so new homes fill up with residents. Returns to normal once homes are full, so it does not create homelessness." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Commercial)), "Commercial" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Commercial)), "Keep commercial demand at maximum, including the per-product demand the game uses to choose which shops to build." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Industrial)), "Industrial" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Industrial)), "Keep industrial (manufacturing) demand at maximum, including per-product demand." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Office)), "Office" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Office)), "Keep office demand at maximum, including per-product demand." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.FillBusinesses)), "Fill empty businesses" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.FillBusinesses)), "Keep new shops, factories and offices arriving for the zone types switched on above, so new buildings get tenants. Stops on its own once there are no empty buildings. Businesses still need workers and customers to stay open." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Warehouses)), "Warehouses" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Warehouses)), "Also keep demand for warehouses on industrial zones. Off by default because warehouses can crowd out factories." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.IgnoreLocation)), "Build anywhere (ignore location and demand)" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.IgnoreLocation)), "Turns on the game's built-in fast-spawn mode: buildings grow on every zoned lot regardless of demand or how attractive the location is. Applies to all zone types, even ones switched off above." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.LogDiagnostics)), "Write diagnostics to log" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.LogDiagnostics)), "Every few seconds of game time, write the current demand values and empty-home counts to Logs/InfiniteZoneDemand.Mod.log. Useful for bug reports." },
            };
        }

        public void Unload()
        {
        }
    }
}
