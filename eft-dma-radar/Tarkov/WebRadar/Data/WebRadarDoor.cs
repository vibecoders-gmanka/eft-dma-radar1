using eft_dma_radar.Tarkov.GameWorld.Interactables;
using eft_dma_radar.Tarkov.WebRadar.Data;
using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace eft_dma_radar.Tarkov.WebRadar.Data
{
    [MessagePackObject]
    public readonly struct WebRadarDoor
    {
        [Key(0)]
        public readonly EDoorState DoorState { get; init; }
        [Key(1)]
        public readonly string Id { get; init; }
        [Key(2)]
        public readonly string? KeyId { get; init; }
        [Key(3)]
        public readonly Vector3 Position { get; init; }

        public static WebRadarDoor CreateFromDoor(Door door)
        {
            return new WebRadarDoor()
            {
                DoorState = door.DoorState,
                Id = door.Id,
                KeyId = door.KeyId,
                Position = door.Position
            };
        }
    }
}
