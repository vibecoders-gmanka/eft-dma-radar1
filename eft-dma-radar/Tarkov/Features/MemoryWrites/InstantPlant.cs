using eft_dma_radar.Tarkov.Features;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc;
using eft_dma_radar.Tarkov.EFTPlayer;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class InstantPlant : MemWriteFeature<InstantPlant>
    {
        private bool _lastEnabledState;
        private ulong _cachedPlantState;

        private const float ORIGINAL_SPEED = 0.3f;
        private const float INSTANT_SPEED = 0.001f;

        public override bool Enabled
        {
            get => MemWrites.Config.InstantPlant;
            set => MemWrites.Config.InstantPlant = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(100);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.LocalPlayer is not LocalPlayer localPlayer)
                    return;

                if (Enabled != _lastEnabledState)
                {
                    var plantState = GetPlantState(localPlayer);
                    if (!plantState.IsValidVirtualAddress())
                        return;

                    var targetSpeed = Enabled ? INSTANT_SPEED : ORIGINAL_SPEED;
                    writes.AddValueEntry(plantState + Offsets.MovementState.PlantTime, targetSpeed);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        LoneLogging.WriteLine($"[InstantPlant] {(Enabled ? "Enabled" : "Disabled")} (Speed: {targetSpeed:F3})");
                    };
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[InstantPlant]: {ex}");
                _cachedPlantState = default;
            }
        }

        private ulong GetPlantState(LocalPlayer localPlayer)
        {
            if (_cachedPlantState.IsValidVirtualAddress())
                return _cachedPlantState;

            var movementContext = localPlayer.MovementContext;
            if (!movementContext.IsValidVirtualAddress())
                return 0x0;

            var plantState = Memory.ReadPtr(movementContext + Offsets.MovementContext.PlantState);
            if (!plantState.IsValidVirtualAddress())
                return 0x0;

            _cachedPlantState = plantState;
            return plantState;
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _cachedPlantState = default;
        }
    }
}