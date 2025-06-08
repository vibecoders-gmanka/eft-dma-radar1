using System;
using System.Linq;
using System.Numerics;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Unity;
using eft_dma_shared.Common.Unity.LowLevel;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_shared.Common.Unity.LowLevel.Hooks;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites.Patches
{
    public sealed class SilentLoot : MemPatchFeature<SilentLoot>
    {
        private static readonly byte[] _retPatch = new byte[] { 0xC3 }; // RET
        private static byte[] _originalBytes;
        private static ulong _methodAddress;
        private static bool _patched;
        private static LootItem _targetItem;

        public override bool Enabled
        {
            get => MemWrites.Config.SilentLoot.Enabled;
            set => MemWrites.Config.SilentLoot.Enabled = value;
        }

        public override bool CanRun => Memory.Ready && Memory.LocalPlayer != null && Memory.Game?.InRaid == true && NativeHook.Initialized;

        protected override Func<ulong> GetPFunc => LookupAndPatchMethod;
        protected override int PFuncSize => 0x100;
        protected override byte[] Signature => null;
        protected override byte[] Patch => _retPatch;
        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(250);

        public override void OnGameStop()
        {
            IsApplied = false;
            OnDisable();
        }

        public void OnDisable()
        {
            if (_patched && _methodAddress.IsValidVirtualAddress())
                Memory.WriteBufferEnsure<byte>(_methodAddress, _originalBytes);

            _patched = false;
        }

        public override bool TryApply()
        {
            if (!Enabled || !CanRun)
                return true;

            var local = Memory.LocalPlayer;
            if (local == null || Memory.Loot?.FilteredLoot == null)
                return false;

            if (!_patched && LookupAndPatchMethod() == 0)
            {
                LoneLogging.WriteLine("[SilentLoot] Failed to patch SaveInteractionRayInfo method.");
                return false;
            }

            _targetItem = FindClosestItem(local);
            if (_targetItem == null)
            {
                LoneLogging.WriteLine("[SilentLoot] No valid loot item found within range.");
                return false;
            }

            float yOffset = MemWrites.Config.SilentLoot.Distance;
            var rayOrigin = _targetItem.Position + new Vector3(0f, yOffset, 0f);
            var rayDir = new Vector3(0f, -1f, 0f);
            ulong usedAddr = _targetItem.InteractiveClass;

            try
            {
                Memory.WriteValue(local.Base + Offsets.Player.InteractionRayOriginOnStartOperation, rayOrigin);
                Memory.WriteValue(local.Base + Offsets.Player.InteractionRayDirectionOnStartOperation, rayDir);
                Memory.WriteValue(local.Base + Offsets.Player.InteractableObject, usedAddr);

                LoneLogging.WriteLine($"[SilentLoot] Silent loot write successful. Target=0x{usedAddr:X}, RayOrigin={rayOrigin}, RayDir={rayDir}");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[SilentLoot] Write failed: {ex.Message}");
                return false;
            }

            return true;
        }

        private static ulong LookupAndPatchMethod()
        {
            LoneLogging.WriteLine("[SilentLoot] Resolving SaveInteractionRayInfo...");

            var cls = MonoLib.MonoClass.Find("Assembly-CSharp", "EFT.Player", out var classAddr);
            if (!classAddr.IsValidVirtualAddress()) return 0;
            if (NativeMethods.CompileClass(classAddr) == 0) return 0;

            var method = cls.FindMethod("SaveInteractionRayInfo");
            if (!method.IsValidVirtualAddress()) return 0;

            _methodAddress = NativeMethods.CompileMethod(method);
            if (!_methodAddress.IsValidVirtualAddress()) return 0;

            _originalBytes = new byte[_retPatch.Length];
            Memory.ReadBufferEnsure<byte>(_methodAddress, _originalBytes);
            Memory.WriteBufferEnsure<byte>(_methodAddress, _retPatch);
            _patched = true;

            LoneLogging.WriteLine($"[SilentLoot] Patched SaveInteractionRayInfo at 0x{_methodAddress:X}");
            return _methodAddress;
        }

        private static LootItem FindClosestItem(LocalPlayer local)
        {
            float closestDist = float.MaxValue;
            LootItem closestItem = null;

            foreach (var entry in Memory.Loot?.FilteredLoot)
            {
                if (entry == null) continue;
                float dist = Vector3.Distance(entry.Position, local.Position);
                if (dist < MemWrites.Config.SilentLoot.MaxDistance && dist < closestDist)
                {
                    closestDist = dist;
                    closestItem = entry;
                }
            }

            return closestItem;
        }
    }
}
