using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Unity;
using System.Threading;
using static SDK.ClassNames;
using static SDK.Offsets;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class MuleMode : MemWriteFeature<MuleMode>
    {
        private bool _lastEnabledState;
        private ulong _cachedPhysical;

        private const float MULE_OVERWEIGHT = 0f;
        private const float MULE_WALK_OVERWEIGHT = 0f;
        private const float MULE_WALK_SPEED_LIMIT = 1f;
        private const float MULE_INERTIA = 0.01f;
        private const float MULE_SPRINT_WEIGHT_FACTOR = 0f;
        private const float MULE_SPRINT_ACCELERATION = 1f;
        private const float MULE_PRE_SPRINT_ACCELERATION = 3f;
        private const float MULE_STATE_SPEED_LIMIT = 1f;
        private const float MULE_STATE_SPRINT_SPEED_LIMIT = 1f;
        private const byte MULE_IS_OVERWEIGHT = 0;

        protected override TimeSpan Delay => TimeSpan.FromSeconds(1);

        public override bool Enabled
        {
            get => MemWrites.Config.MuleMode;
            set => MemWrites.Config.MuleMode = value;
        }

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.Game is not LocalGameWorld game || Memory.LocalPlayer is not LocalPlayer localPlayer)
                    return;

                if (Enabled && Enabled != _lastEnabledState)
                {
                    var physical = GetPhysical(localPlayer);
                    if (!physical.IsValidVirtualAddress())
                        return;

                    var movementContext = localPlayer.MovementContext;
                    if (!movementContext.IsValidVirtualAddress())
                        return;

                    ApplyMuleSettings(writes, physical, movementContext);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        LoneLogging.WriteLine($"[MuleMode] Enabled");
                    };
                }
                else if (!Enabled && _lastEnabledState)
                {
                    _lastEnabledState = false;
                    LoneLogging.WriteLine($"[MuleMode] Disabled");
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[MuleMode]: {ex}");
                _cachedPhysical = default;
            }
        }

        private ulong GetPhysical(LocalPlayer localPlayer)
        {
            if (_cachedPhysical.IsValidVirtualAddress())
                return _cachedPhysical;

            var physical = Memory.ReadPtr(localPlayer.Base + Offsets.Player.Physical);
            if (!physical.IsValidVirtualAddress())
                return 0x0;

            _cachedPhysical = physical;
            return physical;
        }

        private static void ApplyMuleSettings(ScatterWriteHandle writes, ulong physical, ulong movementContext)
        {
            var currentBaseOverweightLimits = Memory.ReadValue<Vector2>(physical + Offsets.Physical.BaseOverweightLimits);
            var overweightLimits = new Vector2(currentBaseOverweightLimits.Y - 1f, currentBaseOverweightLimits.Y);

            writes.AddValueEntry(physical + Offsets.Physical.Overweight, MULE_OVERWEIGHT);
            writes.AddValueEntry(physical + Offsets.Physical.WalkOverweight, MULE_WALK_OVERWEIGHT);
            writes.AddValueEntry(physical + Offsets.Physical.WalkSpeedLimit, MULE_WALK_SPEED_LIMIT);
            writes.AddValueEntry(physical + Offsets.Physical.Inertia, MULE_INERTIA);
            writes.AddValueEntry(physical + Offsets.Physical.SprintWeightFactor, MULE_SPRINT_WEIGHT_FACTOR);
            writes.AddValueEntry(physical + Offsets.Physical.SprintAcceleration, MULE_SPRINT_ACCELERATION);
            writes.AddValueEntry(physical + Offsets.Physical.PreSprintAcceleration, MULE_PRE_SPRINT_ACCELERATION);
            writes.AddValueEntry(physical + Offsets.Physical.BaseOverweightLimits, overweightLimits);
            writes.AddValueEntry(physical + Offsets.Physical.SprintOverweightLimits, overweightLimits);
            writes.AddValueEntry(physical + Offsets.Physical.IsOverweightA, MULE_IS_OVERWEIGHT);
            writes.AddValueEntry(physical + Offsets.Physical.IsOverweightB, MULE_IS_OVERWEIGHT);
            writes.AddValueEntry(movementContext + Offsets.MovementContext.StateSpeedLimit, MULE_STATE_SPEED_LIMIT);
            writes.AddValueEntry(movementContext + Offsets.MovementContext.StateSprintSpeedLimit, MULE_STATE_SPRINT_SPEED_LIMIT);
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _cachedPhysical = default;
        }
    }
}