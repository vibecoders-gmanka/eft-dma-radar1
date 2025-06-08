using System.Collections.Frozen;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.UI.Misc;
using eft_dma_shared.Common.Misc.Data;
using eft_dma_shared.Common.Unity.Collections;

namespace eft_dma_radar.Tarkov.EFTPlayer.Plugins
{
    public sealed class GearManager
    {
        private static readonly FrozenSet<string> THERMAL_IDS = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "5c110624d174af029e69734c", // T7 Thermal Goggles
            "6478641c19d732620e045e17", // ECHO1 1-2x30mm 30Hz thermal reflex
            "609bab8b455afd752b2e6138", // T12W 30Hz thermal reflex sight
            "63fc44e2429a8a166c7f61e6", // Armasight Zeus-Pro 640 2-8x50 30Hz
            "5d1b5e94d7ad1a2b865a96b0", // FLIR RS-32 2.25-9x 35mm 60Hz
            "606f2696f2cb2e02a42aceb1", // MP-155 Ultima thermal camera
            "5a1eaa87fcdbcb001865f75e"  // Trijicon REAP-IR thermal
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenSet<string> NVG_IDS = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "5c066e3a0db834001b7353f0", // N-15 Night Vision
            "5c0696830db834001d23f5da", // PNV-10T Night Vision
            "5c0558060db834001b735271", // GPNVG-18 Night Vision
            "57235b6f24597759bf5a30f1", // AN/PVS-14 Night Vision Monocular
            "5b3b6e495acfc4330140bd88", // Vulcan MG night scope 3.5x
            "5a7c74b3e899ef0014332c29"  // NSPU-M night Scope
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenSet<string> UBGL_IDS = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "62e7e7bbe6da9612f743f1e0", // GP-25 Kostyor 40mm
            "6357c98711fb55120211f7e1", // M203 40mm
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenSet<string> _skipSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SecuredContainer", "Dogtag", "Compass", "ArmBand"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        private readonly bool _isPMC;

        public GearManager(Player player, bool isPMC = false)
        {
            _isPMC = isPMC;
            var slotDict = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
            var inventorycontroller = Memory.ReadPtr(player.InventoryControllerAddr);
            var inventory = Memory.ReadPtr(inventorycontroller + Offsets.InventoryController.Inventory);
            var equipment = Memory.ReadPtr(inventory + Offsets.Inventory.Equipment);
            var slots = Memory.ReadPtr(equipment + Offsets.Equipment.Slots);
            using var slotsArray = MemArray<ulong>.Get(slots);

            foreach (var slotPtr in slotsArray)
            {
                var namePtr = Memory.ReadPtr(slotPtr + Offsets.Slot.ID);
                var name = Memory.ReadUnityString(namePtr);
                if (_skipSlots.Contains(name))
                    continue;
                slotDict.TryAdd(name, slotPtr);
            }

            Slots = slotDict;
            Refresh();
        }

        private IReadOnlyDictionary<string, ulong> Slots { get; }

        /// <summary>
        /// List of equipped items in Player Inventory Slots.
        /// </summary>
        public IReadOnlyDictionary<string, GearItem> Equipment { get; private set; }

        /// <summary>
        /// Player's contained gear/loot.
        /// </summary>
        public IReadOnlyList<LootItem> Loot { get; private set; }

        /// <summary>
        /// True if Quest Items are contained in this loot pool.
        /// </summary>
        public bool HasQuestItems => Loot?.Any(x => x.IsQuestCondition) ?? false;

        /// <summary>
        /// True if NVG item are contained in this loot pool.
        /// </summary>
        public bool HasNVG { get; private set; }

        /// <summary>
        /// True if Thermal item is contained in this loot pool.
        /// </summary>
        public bool HasThermal { get; private set; }

        /// <summary>
        /// True if Underbarrel Grenade Launcher is contained in this loot pool.
        /// </summary>
        public bool HasUBGL { get; private set; }

        /// <summary>
        /// Value of this player's Gear/Loot.
        /// </summary>
        public int Value { get; private set; }

        public void Refresh()
        {
            var loot = new List<LootItem>();
            var gearDict = new Dictionary<string, GearItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var slot in Slots)
                try
                {
                    if (_isPMC && slot.Key == "Scabbard")
                        continue; // skip pmc scabbard

                    var containedItem = Memory.ReadPtr(slot.Value + Offsets.Slot.ContainedItem);
                    var inventorytemplate = Memory.ReadPtr(containedItem + Offsets.LootItem.Template);
                    var idPtr = Memory.ReadValue<Types.MongoID>(inventorytemplate + Offsets.ItemTemplate._id);
                    var id = Memory.ReadUnityString(idPtr.StringID);

                    if (EftDataManager.AllItems.TryGetValue(id, out var entry1))
                        loot.Add(new LootItem(entry1));

                    try // Get all items on player
                    {
                        var grids = Memory.ReadValue<ulong>(containedItem + Offsets.LootItemMod.Grids);
                        LootManager.GetItemsInGrid(grids, loot);
                    }
                    catch { }

                    if (EftDataManager.AllItems.TryGetValue(id, out var entry2))
                    {
                        if (slot.Key == "FirstPrimaryWeapon" || slot.Key == "SecondPrimaryWeapon" ||
                            slot.Key == "Holster" || slot.Key == "Headwear") // Only interested in weapons / helmets
                            try
                            {
                                RecursePlayerGearSlots(containedItem, loot);
                            }
                            catch { }

                        var gear = new GearItem
                        {
                            Long = entry2.Name ?? "None",
                            Short = entry2.ShortName ?? "None"
                        };
                        gearDict.TryAdd(slot.Key, gear);
                    }
                }
                catch { } // Skip over empty slots

            Loot = loot.OrderLoot().ToList();
            Value = loot.Sum(x => x.Price); // Get value of player's loot/gear
            Equipment = gearDict; // update readonly ref
            HasNVG = Loot.Any(x => NVG_IDS.Contains(x.ID));
            HasThermal = Loot.Any(x => THERMAL_IDS.Contains(x.ID));
            HasUBGL = Loot.Any(x => UBGL_IDS.Contains(x.ID));
        }

        /// <summary>
        /// Checks a 'Primary' weapon for Ammo Type, and Thermal Scope.
        /// </summary>
        private static void RecursePlayerGearSlots(ulong lootItemBase, List<LootItem> loot)
        {
            try
            {
                var parentSlots = Memory.ReadPtr(lootItemBase + Offsets.LootItemMod.Slots);
                using var slotsArray = MemArray<ulong>.Get(parentSlots);
                var slotDict = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);

                foreach (var slotPtr in slotsArray)
                {
                    var namePtr = Memory.ReadPtr(slotPtr + Offsets.Slot.ID);
                    var name = Memory.ReadUnityString(namePtr);
                    slotDict.TryAdd(name, slotPtr);
                }

                foreach (var slotName in slotDict.Keys)
                    try
                    {
                        if (slotDict.TryGetValue(slotName, out var slot))
                        {
                            var containedItem = Memory.ReadPtr(slot + Offsets.Slot.ContainedItem);
                            var inventorytemplate = Memory.ReadPtr(containedItem + Offsets.LootItem.Template);
                            var idPtr = Memory.ReadValue<Types.MongoID>(inventorytemplate + Offsets.ItemTemplate._id);
                            var id = Memory.ReadUnityString(idPtr.StringID);
                            if (EftDataManager.AllItems.TryGetValue(id, out var entry))
                                loot.Add(new LootItem(entry)); // Add to loot, get weapon attachment values
                            RecursePlayerGearSlots(containedItem, loot);
                        }
                    }
                    catch
                    {
                    } // Skip over empty slots
            }
            catch
            {
            }
        }
    }
}