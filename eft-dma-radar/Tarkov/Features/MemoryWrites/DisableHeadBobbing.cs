using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Unity;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class DisableHeadBobbing : MemWriteFeature<DisableHeadBobbing>
    {
        private bool _lastEnabledState;
        private ulong _cachedSettingPtr;

        private const float DEFAULT_HEADBOBBING_VALUE = 0.2f;
        private const float DISABLED_HEADBOBBING_VALUE = 0f;

        public override bool Enabled
        {
            get => MemWrites.Config.DisableHeadBobbing;
            set => MemWrites.Config.DisableHeadBobbing = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromSeconds(1);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                var settingPtr = GetHeadBobbingSettingPtr();
                if (!settingPtr.IsValidVirtualAddress())
                    return;

                if (Enabled != _lastEnabledState)
                {
                    var targetValue = Enabled ? DISABLED_HEADBOBBING_VALUE : DEFAULT_HEADBOBBING_VALUE;

                    writes.AddValueEntry(settingPtr + Offsets.BSGGameSettingValueClass.Value, targetValue);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        LoneLogging.WriteLine($"[DisableHeadBobbing] {(Enabled ? "Enabled" : "Disabled")} (Value: {targetValue:F1})");
                    };
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[DisableHeadBobbing]: {ex}");
                _cachedSettingPtr = default;
            }
        }

        private ulong GetHeadBobbingSettingPtr()
        {
            if (_cachedSettingPtr.IsValidVirtualAddress())
                return _cachedSettingPtr;

            try
            {
                var gameSettings = Memory.ReadPtr(MonoLib.Singleton.FindOne(ClassNames.GameSettings.ClassName));
                if (!gameSettings.IsValidVirtualAddress())
                    return 0x0;

                var game = Memory.ReadPtr(gameSettings + Offsets.GameSettingsContainer.Game);
                if (!game.IsValidVirtualAddress())
                    return 0x0;

                var settings = Memory.ReadPtr(game + Offsets.GameSettingsInnerContainer.Settings);
                if (!settings.IsValidVirtualAddress())
                    return 0x0;

                var headBobbing = Memory.ReadPtr(settings + Offsets.GameSettings.HeadBobbing);
                if (!headBobbing.IsValidVirtualAddress())
                    return 0x0;

                var value = Memory.ReadPtr(headBobbing + Offsets.BSGGameSetting.ValueClass);
                if (!value.IsValidVirtualAddress())
                    return 0x0;

                _cachedSettingPtr = value;
                LoneLogging.WriteLine($"[DisableHeadBobbing] Resolved setting pointer: 0x{_cachedSettingPtr:X}");
                return value;
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[DisableHeadBobbing] Failed to resolve setting pointer: {ex}");
                return 0x0;
            }
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _cachedSettingPtr = default;
        }
    }
}