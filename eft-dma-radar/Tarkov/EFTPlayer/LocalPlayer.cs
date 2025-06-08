using eft_dma_shared.Common.Misc;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;

using eft_dma_shared.Common.DMA;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Players;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.Collections;

namespace eft_dma_radar.Tarkov.EFTPlayer
{
    public sealed class LocalPlayer : ClientPlayer, ILocalPlayer
    {
        /// <summary>
        /// All Items on the Player's WishList.
        /// </summary>
        public static IReadOnlySet<string> WishlistItems => _wishlistItems;
        private static HashSet<string> _wishlistItems = new(StringComparer.OrdinalIgnoreCase);

        private ulong HealthController { get; }

        private ulong _energyPtr = 0;
        private ulong _hydrationPtr = 0;

        private float _cachedEnergy = 0f;
        private float _cachedHydration = 0f;
        private DateTime _lastEnergyHydrationRead = DateTime.MinValue;
        private readonly TimeSpan _energyHydrationCacheTime = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Spawn Point.
        /// </summary>
        public string EntryPoint { get; }
        /// <summary>
        /// Profile ID (if Player Scav).
        /// Used for Exfils.
        /// </summary>
        public string ProfileId { get; }
        /// <summary>
        /// Firearm Information.
        /// </summary>
        public FirearmManager Firearm { get; }
        /// <summary>
        /// Player name.
        /// </summary>
        public override string Name
        {
            get => "localPlayer";
            set { }
        }
        /// <summary>
        /// Player is Human-Controlled.
        /// </summary>
        public override bool IsHuman { get; }

        public LocalPlayer(ulong playerBase) : base(playerBase)
        {
            string classType = ObjectClass.ReadName(this);
            if (!(classType == "LocalPlayer" || classType == "ClientPlayer"))
                throw new ArgumentOutOfRangeException(nameof(classType));

            IsHuman = true;

            this.Firearm = new(this);

            if (IsPmc)
            {
                var entryPtr = Memory.ReadPtr(Info + Offsets.PlayerInfo.EntryPoint);
                EntryPoint = Memory.ReadUnityString(entryPtr);
            }
            else if (IsScav)
            {
                var profileIdPtr = Memory.ReadPtr(this.Profile + Offsets.Profile.Id);
                ProfileId = Memory.ReadUnityString(profileIdPtr);
            }

            this.HealthController = Memory.ReadPtr(this + Offsets.Player._healthController);

            _energyPtr = Memory.ReadPtr(this.HealthController + Offsets.HealthSystem.Energy);
            _hydrationPtr = Memory.ReadPtr(this.HealthController + Offsets.HealthSystem.Hydration);

            ulong id = ulong.Parse(AccountID);
            ILocalPlayer.AccountId = id;
        }

        /// <summary>
        /// Set the Player's WishList.
        /// </summary>
        public void RefreshWishlist()
        {
            var wishlistManager = Memory.ReadPtr(this.Profile + Offsets.Profile.WishlistManager);
            var itemsPtr = Memory.ReadPtr(wishlistManager + Offsets.WishlistManager.Items);
            using var items = MemDictionary<Types.MongoID, int>.Get(itemsPtr);
            var wishlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                try
                {
                    if (item.Key.StringID == 0)
                        continue;
                    string id = Memory.ReadUnityString(item.Key.StringID);
                    if (string.IsNullOrWhiteSpace(id))
                        continue;
                    wishlist.Add(id);
                }
                catch { }
            }
            _wishlistItems = wishlist;
        }

        /// <summary>
        /// Additional realtime reads for LocalPlayer.
        /// </summary>
        /// <param name="index"></param>
        public override void OnRealtimeLoop(ScatterReadIndex index)
        {
            index.AddEntry<MemPointer>(-10, this.MovementContext + Offsets.MovementContext.CurrentState);
            index.AddEntry<MemPointer>(-11, this.HandsControllerAddr);
            index.Callbacks += x1 =>
            {
                if (x1.TryGetResult<MemPointer>(-10, out var currentState))
                    ILocalPlayer.PlayerState = currentState;
                if (x1.TryGetResult<MemPointer>(-11, out var handsController))
                    ILocalPlayer.HandsController = handsController;
            };
            Firearm.OnRealtimeLoop(index);
            base.OnRealtimeLoop(index);
        }

        /// <summary>
        /// Get View Angles for LocalPlayer.
        /// </summary>
        /// <returns>View Angles (Vector2).</returns>
        public Vector2 GetViewAngles() =>
            Memory.ReadValue<Vector2>(this.RotationAddress, false);

        /// <summary>
        /// Checks if LocalPlayer is Aiming (ADS).
        /// </summary>
        /// <returns>True if aiming (ADS), otherwise False.</returns>
        public bool CheckIfADS()
        {
            try
            {
                return Memory.ReadValue<bool>(this.PWA + Offsets.ProceduralWeaponAnimation._isAiming, false);
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"CheckIfADS() ERROR: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Gets the current energy level (cached, updated every 3 seconds).
        /// </summary>
        /// <returns>Energy level as a float.</returns>
        public float GetEnergy()
        {
            try
            {
                if (DateTime.UtcNow - _lastEnergyHydrationRead >= _energyHydrationCacheTime)
                    UpdateEnergyHydrationCache();

                return _cachedEnergy;
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"GetEnergy() ERROR: {ex}");
                return _cachedEnergy;
            }
        }

        /// <summary>
        /// Gets the current hydration level (cached, updated every 3 seconds).
        /// </summary>
        /// <returns>Hydration level as a float.</returns>
        public float GetHydration()
        {
            try
            {
                if (DateTime.UtcNow - _lastEnergyHydrationRead >= _energyHydrationCacheTime)
                    UpdateEnergyHydrationCache();

                return _cachedHydration;
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"GetHydration() ERROR: {ex}");
                return _cachedHydration;
            }
        }

        /// <summary>
        /// Updates the cached energy and hydration values from memory.
        /// </summary>
        private void UpdateEnergyHydrationCache()
        {
            try
            {
                var energyStruct = Memory.ReadValue<Types.HealthSystem>(_energyPtr + Offsets.HealthValue.Value);
                var hydrationStruct = Memory.ReadValue<Types.HealthSystem>(_hydrationPtr + Offsets.HealthValue.Value);
                _cachedEnergy = energyStruct.Current;
                _cachedHydration = hydrationStruct.Current;
                _lastEnergyHydrationRead = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"UpdateEnergyHydrationCache() ERROR: {ex}");
            }
        }
    }
}
