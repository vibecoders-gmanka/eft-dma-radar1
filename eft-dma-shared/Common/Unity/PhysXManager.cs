using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using eft_dma_shared.Common.DMA;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Unity.LowLevel.Hooks;
using static eft_dma_shared.Common.Unity.MonoLib;

namespace eft_dma_shared.Common.Unity.LowLevel.PhysX
{
    public static class PhysXManager
    {
        private static readonly object _lock = new();
        private static List<Actor> _cachedActors = new();
        private static ulong _physxSceneImpl = 0;

        public enum GeometryType
        {
            Box = 3,
            Sphere = 0,
            Capsule = 2,
        }

        public struct Actor
        {
            public GeometryType Type;
            public Vector3 Position;
            public Vector3 HalfExtents;
            public float Radius;
            public float HalfHeight;
        }

        public struct HitResult
        {
            public bool DidHit;
            public Vector3 Point;
            public float Distance;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PhysicsSceneInjectedResult
        {
            public ulong m_Handle;
        }

        public static void Initialize()
        {
            try
            {
                // Resolve class
                var klass = MonoClass.Find("UnityEngine.PhysicsModule", "UnityEngine.Physics", out ulong classAddr);
                if (classAddr == 0)
                    throw new Exception("Physics class not found");
    
                if (NativeMethods.CompileClass(classAddr) == 0)
                    throw new Exception("Failed to compile Physics class");
    
                // Find and JIT method
                var method = klass.FindMethod("get_defaultPhysicsScene_Injected");
                if (method == 0)
                    throw new Exception("Method not found");
    
                var jitted = NativeMethods.CompileMethod(method);
                if (jitted == 0)
                    throw new Exception("Failed to JIT method");
    
                LoneLogging.WriteLine($"[PhysX] Jitted get_defaultPhysicsScene_Injected @ 0x{jitted:X}");
    
                // Allocate buffer for the out struct
                ulong resultAddr = NativeMethods.AllocZero((uint)Marshal.SizeOf<PhysicsSceneInjectedResult>());
                if (!resultAddr.IsValidVirtualAddress())
                    throw new Exception("Failed to allocate result memory");
    
                // Call the JIT'd method (expects ref PhysicsScene)
                var callSuccess = NativeHook.Call(jitted, resultAddr);
                if (callSuccess == null)
                    throw new Exception("Call to get_defaultPhysicsScene_Injected failed");
    
                // Read back PhysicsSceneInjectedResult struct
                var result = Memory.Read<PhysicsSceneInjectedResult>(resultAddr);
    
                // Read the actual PhysX SDK address (the handle is actually a pointer)
                _physxSceneImpl = (ulong)result.m_Handle;
                if (!_physxSceneImpl.IsValidVirtualAddress())
                    throw new Exception($"Invalid PhysX SDK address: 0x{_physxSceneImpl:X}");
    
                LoneLogging.WriteLine($"[PhysX] NpScene base: 0x{_physxSceneImpl:X}");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[PhysX] Initialization failed: {ex.Message}");
            }
        }

        public static void CacheActors()
        {
            if (!Utils.IsValidVirtualAddress(_physxSceneImpl))
            {
                LoneLogging.WriteLine("[PhysX] SceneImpl is invalid, cannot cache actors.");
                return;
            }
            {
                LoneLogging.WriteLine("[PhysX] SceneImpl invalid, reinitializing...");
                Initialize();
                if (!Utils.IsValidVirtualAddress(_physxSceneImpl))
                {
                    LoneLogging.WriteLine("[PhysX] Still invalid. Aborting actor caching.");
                    return;
                }
            }

            var actors = new List<Actor>();
            try
            {
                var rigidArray = Memory.ReadValue<ulong>(_physxSceneImpl + 0x23D8);
                var rigidCount = Memory.ReadValue<int>(_physxSceneImpl + 0x23E0);

                for (int j = 0; j < rigidCount; j++)
                {
                    var actorPtr = Memory.ReadValue<ulong>(rigidArray + (ulong)j * 8);
                    if (!actorPtr.IsValidVirtualAddress()) continue;

                    var type = (GeometryType)Memory.ReadValue<ushort>(actorPtr + 0x8);
                    if (type != GeometryType.Sphere && type != GeometryType.Box && type != GeometryType.Capsule) continue;

                    var shapeManager = Memory.ReadValue<ulong>(actorPtr + 0x120);
                    var shapePtr = Memory.ReadValue<ulong>(shapeManager);
                    if (!shapePtr.IsValidVirtualAddress()) continue;

                    var geomType = (GeometryType)Memory.ReadValue<int>(shapePtr + 0x70);
                    var transform = Memory.ReadValue<Vector3>(shapePtr + 0x30);

                    Actor actor = new()
                    {
                        Type = geomType,
                        Position = transform,
                    };

                    switch (geomType)
                    {
                        case GeometryType.Sphere:
                            actor.Radius = Memory.ReadValue<float>(shapePtr + 0x78);
                            break;
                        case GeometryType.Box:
                            actor.HalfExtents = Memory.ReadValue<Vector3>(shapePtr + 0x78);
                            break;
                        case GeometryType.Capsule:
                            actor.Radius = Memory.ReadValue<float>(shapePtr + 0x78);
                            actor.HalfHeight = Memory.ReadValue<float>(shapePtr + 0x7C);
                            break;
                    }

                    actors.Add(actor);
                }

                lock (_lock)
                {
                    _cachedActors = actors;
                }

                LoneLogging.WriteLine($"[PhysXManager] Cached {actors.Count} actors");
            }
            catch (Exception e)
            {
                LoneLogging.WriteLine($"[PhysXManager] CacheActors exception: {e}");
            }
        }

        public static HitResult Raycast(Vector3 origin, Vector3 direction)
        {
            direction = Vector3.Normalize(direction);
            float bestDist = float.MaxValue;
            Vector3 hitPoint = Vector3.Zero;
            bool didHit = false;

            lock (_lock)
            {
                foreach (var actor in _cachedActors)
                {
                    float t = -1;
                    switch (actor.Type)
                    {
                        case GeometryType.Sphere:
                            t = RaySphere(origin, direction, actor.Position, actor.Radius);
                            break;
                        case GeometryType.Box:
                            t = RayBox(origin, direction, actor.Position, actor.HalfExtents);
                            break;
                    }

                    if (t > 0 && t < bestDist)
                    {
                        bestDist = t;
                        hitPoint = origin + direction * t;
                        didHit = true;
                    }
                }
            }

            return new HitResult
            {
                DidHit = didHit,
                Point = hitPoint,
                Distance = bestDist
            };
        }

        public static bool IsVisible(Vector3 from, Vector3 to)
        {
            var dir = to - from;
            var hit = Raycast(from, dir);
            return !hit.DidHit || Vector3.Distance(hit.Point, to) < 0.05f;
        }

        private static float RaySphere(Vector3 origin, Vector3 dir, Vector3 center, float radius)
        {
            var oc = origin - center;
            float a = Vector3.Dot(dir, dir);
            float b = 2.0f * Vector3.Dot(oc, dir);
            float c = Vector3.Dot(oc, oc) - radius * radius;
            float discriminant = b * b - 4 * a * c;
            if (discriminant < 0) return -1f;
            float t = (-b - MathF.Sqrt(discriminant)) / (2.0f * a);
            return t > 0 ? t : -1f;
        }

        private static float RayBox(Vector3 origin, Vector3 dir, Vector3 boxCenter, Vector3 halfExtents)
        {
            Vector3 min = boxCenter - halfExtents;
            Vector3 max = boxCenter + halfExtents;

            float tmin = float.MinValue;
            float tmax = float.MaxValue;

            for (int i = 0; i < 3; i++)
            {
                float o = origin[i];
                float d = dir[i];
                float minVal = min[i];
                float maxVal = max[i];

                if (Math.Abs(d) < 1e-8f)
                {
                    if (o < minVal || o > maxVal)
                        return -1f;
                }
                else
                {
                    float t1 = (minVal - o) / d;
                    float t2 = (maxVal - o) / d;
                    if (t1 > t2) (t1, t2) = (t2, t1);
                    tmin = MathF.Max(tmin, t1);
                    tmax = MathF.Min(tmax, t2);
                    if (tmin > tmax)
                        return -1f;
                }
            }

            return tmin > 0 ? tmin : -1f;
        }
    }
}
