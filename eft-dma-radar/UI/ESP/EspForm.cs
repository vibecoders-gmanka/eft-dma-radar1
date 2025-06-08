using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.Features.MemoryWrites;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.GameWorld.Exits;
using eft_dma_radar.Tarkov.GameWorld.Explosives;
using eft_dma_radar.Tarkov.GameWorld.Interactables;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Pages;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.Data;
using eft_dma_shared.Common.Unity;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Switch = eft_dma_radar.Tarkov.GameWorld.Exits.Switch;

namespace eft_dma_radar.UI.ESP
{
    public partial class ESPForm : Form
    {
        #region Fields/Properties/Constructor

        public static bool ShowESP = true;
        private readonly Stopwatch _fpsSw = new();
        private readonly PrecisionTimer _renderTimer;
        private int _fpsCounter;
        private int _fps;

        private volatile bool _espIsRendering = false;

        private SKGLControl skglControl_ESP;

        private readonly ConcurrentBag<SKPath> _pathPool = new ConcurrentBag<SKPath>();

        /// <summary>
        /// Singleton Instance of EspForm.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal static ESPForm Window { get; private set; }

        /// <summary>
        ///  App Config.
        /// </summary>
        private static Config Config => Program.Config;

        /// <summary>
        ///  App Config.
        /// </summary>
        public static ESPConfig ESPConfig { get; } = Config.ESP;

        /// <summary>
        /// True if ESP Window is Fullscreen.
        /// </summary>
        public bool IsFullscreen =>
            FormBorderStyle is FormBorderStyle.None;

        /// <summary>
        /// Map Identifier of Current Map.
        /// </summary>
        private static string MapID
        {
            get
            {
                var id = Memory.MapID;
                id ??= "MAPDEFAULT";
                return id;
            }
        }

        /// <summary>
        /// LocalPlayer (who is running Radar) 'Player' object.
        /// </summary>
        private static LocalPlayer LocalPlayer =>
            Memory.LocalPlayer;

        /// <summary>
        /// All Players in Local Game World (including dead/exfil'd) 'Player' collection.
        /// </summary>
        private static IReadOnlyCollection<Player> AllPlayers => Memory.Players;

        /// <summary>
        /// Contains all 'Hot' grenades in Local Game World, and their position(s).
        /// </summary>
        private static IReadOnlyCollection<IExplosiveItem> Explosives => Memory.Explosives;

        /// <summary>
        /// Contains all 'Exfils' in Local Game World, and their status/position(s).
        /// </summary>
        private static IReadOnlyCollection<IExitPoint> Exits => Memory.Exits;

        /// <summary>
        /// Contains all filtered loot in Local Game World.
        /// </summary>
        private static IEnumerable<LootItem> Loot => Memory.Loot?.FilteredLoot;

        /// <summary>
        /// Contains all static containers in Local Game World.
        /// </summary>
        private static IEnumerable<StaticLootContainer> Containers => Memory.Loot?.StaticLootContainers;

        private static EntityTypeSettingsESP MineSettings = ESP.Config.EntityTypeESPSettings.GetSettings("Mine");

        public ESPForm()
        {
            InitializeComponent();

            skglControl_ESP = new SKGLControl();
            skglControl_ESP.Name = "skglControl_ESP";
            skglControl_ESP.BackColor = Color.Black;
            skglControl_ESP.Dock = DockStyle.Fill;
            skglControl_ESP.Location = new Point(0, 0);
            skglControl_ESP.Margin = new Padding(4, 3, 4, 3);
            skglControl_ESP.Size = new Size(624, 441);
            skglControl_ESP.TabIndex = 0;
            skglControl_ESP.VSync = false;

            this.Controls.Add(skglControl_ESP);

            CenterToScreen();
            skglControl_ESP.DoubleClick += ESP_DoubleClick;
            _fpsSw.Start();

            var allScreens = Screen.AllScreens;
            if (ESPConfig.AutoFullscreen && ESPConfig.SelectedScreen < allScreens.Length)
            {
                var screen = allScreens[ESPConfig.SelectedScreen];
                var bounds = screen.Bounds;
                FormBorderStyle = FormBorderStyle.None;
                Location = new Point(bounds.Left, bounds.Top);
                Size = CameraManagerBase.Viewport.Size;
            }

            var interval = ESPConfig.FPSCap == 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(1000d / ESPConfig.FPSCap);

            _renderTimer = new PrecisionTimer(interval);

            this.Shown += ESPForm_Shown;
        }

        private async void ESPForm_Shown(object sender, EventArgs e)
        {
            while (!this.IsHandleCreated)
                await Task.Delay(25);

            Window ??= this;
            CameraManagerBase.EspRunning = true;

            _renderTimer.Start();

            skglControl_ESP.PaintSurface += ESP_PaintSurface;
            _renderTimer.Elapsed += RenderTimer_Elapsed;
        }

        private void RenderTimer_Elapsed(object sender, EventArgs e)
        {
            if (_espIsRendering || this.IsDisposed) return;

            try
            {
                this.BeginInvoke(new Action(() =>
                {
                    if (_espIsRendering || this.IsDisposed) return;

                    _espIsRendering = true;
                    try
                    {
                        skglControl_ESP.Invalidate();
                    }
                    finally
                    {
                        _espIsRendering = false;
                    }
                }));
            }
            catch
            {
                _espIsRendering = false;
            }
        }

        #endregion

        #region Resource Management

        private SKPath GetPath()
        {
            if (_pathPool.TryTake(out var path))
            {
                path.Reset();
                return path;
            }
            return new SKPath();
        }

        private void ReturnPath(SKPath path)
        {
            if (path != null)
            {
                path.Reset();
                _pathPool.Add(path);
            }
        }

        #endregion

        #region Form Methods

        public void UpdateRenderTimerInterval(int targetFPS)
        {
            var interval = TimeSpan.FromMilliseconds(1000d / targetFPS);
            _renderTimer.Interval = interval;
        }

        /// <summary>
        /// Purge SkiaSharp Resources.
        /// </summary>
        public void PurgeSKResources()
        {
            if (this.IsDisposed) return;

            this.Invoke(() =>
            {
                skglControl_ESP?.GRContext?.PurgeResources();
            });
        }

        /// <summary>
        /// Toggles Full Screen mode for ESP Window.
        /// </summary>
        private void SetFullscreen(bool toFullscreen)
        {
            const int minWidth = 640;
            const int minHeight = 480;
            var screen = Screen.FromControl(this);
            Rectangle view;

            if (toFullscreen)
            {
                FormBorderStyle = FormBorderStyle.None;
                view = CameraManagerBase.Viewport;

                if (view.Width < minWidth)
                    view.Width = minWidth;
                if (view.Height < minHeight)
                    view.Height = minHeight;
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                view = new Rectangle(screen.Bounds.X, screen.Bounds.Y, minWidth, minHeight);
            }

            WindowState = FormWindowState.Normal;
            Location = new Point(screen.Bounds.Left, screen.Bounds.Top);
            Width = view.Width;
            Height = view.Height;

            if (!toFullscreen)
                CenterToScreen();
        }

        /// <summary>
        /// Record the Rendering FPS.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetFPS()
        {
            if (_fpsSw.ElapsedMilliseconds >= 1000)
            {
                _fps = Interlocked.Exchange(ref _fpsCounter, 0); // Get FPS -> Reset FPS counter
                _fpsSw.Restart();
            }
            else
            {
                _fpsCounter++;
            }
        }

        /// <summary>
        /// Handle double click even on ESP Window (toggles fullscreen).
        /// </summary>
        private void ESP_DoubleClick(object sender, EventArgs e) => SetFullscreen(!IsFullscreen);

        /// <summary>
        /// Main ESP Render Event.
        /// </summary>
        private void ESP_PaintSurface(object sender, SKPaintGLSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            SetFPS();
            SkiaResourceTracker.TrackESPFrame();
            canvas.Clear(InterfaceColorOptions.FuserBackgroundColor);
            try
            {
                var localPlayer = LocalPlayer;
                var allPlayers = AllPlayers;
                if (localPlayer is not null && allPlayers is not null)
                {
                    if (!ShowESP)
                    {
                        DrawNotShown(canvas);
                    }
                    else
                    {
                        var battleMode = Config.BattleMode;

                        if (!battleMode && (Config.ProcessLoot &&
                            (LootItem.CorpseESPSettings.Enabled ||
                            LootItem.LootESPSettings.Enabled ||
                            LootItem.ImportantLootESPSettings.Enabled ||
                            LootItem.QuestItemESPSettings.Enabled)))
                            DrawLoot(canvas, localPlayer);
                        if (!battleMode && Config.Containers.Show && StaticLootContainer.ESPSettings.Enabled)
                            DrawContainers(canvas, localPlayer);
                        if (!battleMode &&  (Config.QuestHelper.Enabled &&
                            (QuestManager.ESPSettings.Enabled) ||
                            LootItem.QuestItemESPSettings.Enabled))
                            DrawQuests(canvas, localPlayer);
                        if (MineSettings.Enabled && GameData.Mines.TryGetValue(MapID, out var mines))
                            DrawMines(canvas, localPlayer, mines);
                        if (!battleMode && (Exfil.ESPSettings.Enabled ||
                            TransitPoint.ESPSettings.Enabled))
                            DrawExfils(canvas, localPlayer);
                        if (!battleMode && Switch.ESPSettings.Enabled)                        
                            DrawSwitches(canvas, localPlayer);
                        if (!battleMode && Door.ESPSettings.Enabled)          
                            DrawDoors(canvas, localPlayer);
                        if (Grenade.ESPSettings.Enabled ||
                            Tripwire.ESPSettings.Enabled ||
                            MortarProjectile.ESPSettings.Enabled)
                            DrawExplosives(canvas, localPlayer);
                        foreach (var player in allPlayers)
                            player.DrawESP(canvas, localPlayer);
                        if (ESPConfig.ShowRaidStats)
                            DrawRaidStats(canvas, allPlayers);
                        if (ESPConfig.ShowAimFOV && MemWriteFeature<Aimbot>.Instance.Enabled)
                            DrawAimFOV(canvas);
                        if (ESPConfig.ShowFPS)
                            DrawFPS(canvas);
                        if (ESPConfig.ShowMagazine)
                            DrawMagazine(canvas, localPlayer);
                        if (ESPConfig.ShowFireportAim &&
                            !CameraManagerBase.IsADS &&
                            !(ESP.Config.ShowAimLock && MemWriteFeature<Aimbot>.Instance.Cache?.AimbotLockedPlayer is not null))
                            DrawFireportAim(canvas, localPlayer);
                        if (ESPConfig.ShowStatusText)
                            DrawStatusText(canvas);
                        if (ESPConfig.Crosshair.Enabled)
                            DrawCrosshair(canvas);
                        if (ESPConfig.EnergyHydrationBar)
                            DrawEnergyHydration(canvas, localPlayer);
                    }
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"ESP RENDER CRITICAL ERROR: {ex}");
            }
            canvas.Flush();
        }

        /// <summary>
        /// Draws a crosshair at the center of the screen based on selected style.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawCrosshair(SKCanvas canvas)
        {
            if (skglControl_ESP.Width <= 0 || skglControl_ESP.Height <= 0 || !ESPConfig.Crosshair.Enabled)
                return;

            var centerX = skglControl_ESP.Width / 2f;
            var centerY = skglControl_ESP.Height / 2f;
            var size = 10 * ESPConfig.Crosshair.Scale;
            var thickness = 2 * ESPConfig.Crosshair.Scale;
            var dotSize = 3 * ESPConfig.Crosshair.Scale;

            switch (ESPConfig.Crosshair.Type)
            {
                case 0: // Plus (+)
                    canvas.DrawLine(centerX - size, centerY, centerX + size, centerY, SKPaints.PaintCrosshairESP);
                    canvas.DrawLine(centerX, centerY - size, centerX, centerY + size, SKPaints.PaintCrosshairESP);
                    break;
                case 1: // Cross (X)
                    canvas.DrawLine(centerX - size, centerY - size, centerX + size, centerY + size, SKPaints.PaintCrosshairESP);
                    canvas.DrawLine(centerX + size, centerY - size, centerX - size, centerY + size, SKPaints.PaintCrosshairESP);
                    break;
                case 2: // Circle
                    canvas.DrawCircle(centerX, centerY, size, SKPaints.PaintCrosshairESP);
                    break;
                case 3: // Dot
                    canvas.DrawCircle(centerX, centerY, dotSize, SKPaints.PaintCrosshairESPDot);
                    break;
                case 4: // Square
                    var rect = new SKRect(centerX - size, centerY - size, centerX + size, centerY + size);
                    canvas.DrawRect(rect, SKPaints.PaintCrosshairESP);
                    break;
                case 5: // Diamond
                    var path = GetPath();
                    path.MoveTo(centerX, centerY - size);
                    path.LineTo(centerX + size, centerY);
                    path.LineTo(centerX, centerY + size);
                    path.LineTo(centerX - size, centerY);
                    path.Close();
                    canvas.DrawPath(path, SKPaints.PaintCrosshairESP);
                    ReturnPath(path);
                    break;
            }
        }

        /// <summary>
        /// Draw status text on ESP Window (top middle of screen).
        /// </summary>
        /// <param name="canvas"></param>
        private void DrawStatusText(SKCanvas canvas)
        {
            try
            {
                bool aimEnabled = MemWriteFeature<Aimbot>.Instance.Enabled;

                var mode = Aimbot.Config.TargetingMode;
                string label = null;
                if (Config.MemWritesEnabled && Config.MemWrites.RageMode)
                    label = MemWriteFeature<Aimbot>.Instance.Enabled ? $"{mode.GetDescription()}: RAGE MODE" : "RAGE MODE";
                else if (aimEnabled)
                {
                    if (Aimbot.Config.RandomBone.Enabled)
                        label = $"{mode.GetDescription()}: Random Bone";
                    else if (Aimbot.Config.SilentAim.AutoBone)
                        label = $"{mode.GetDescription()}: Auto Bone";
                    else
                    {
                        var defaultBone = Aimbot.Config.Bone;
                        label = $"{mode.GetDescription()}: {defaultBone.GetDescription()}";
                    }
                }
                if (MemWrites.Enabled)
                {
                    if (MemWriteFeature<WideLean>.Instance.Enabled)
                    {
                        if (label is null)
                            label = "Lean";
                        else
                            label += " (Lean)";
                    }
                    if (MemWriteFeature<LootThroughWalls>.Instance.Enabled && LootThroughWalls.ZoomEngaged)
                    {
                        if (label is null)
                            label = "LTW";
                        else
                            label += " (LTW)";
                    }
                    else if (MemWriteFeature<MoveSpeed>.Instance.Enabled)
                    {
                        if (label is null)
                            label = "MOVE";
                        else
                            label += " (MOVE)";
                    }
                }
                if (label is null)
                    return;

                var clientArea = skglControl_ESP.ClientRectangle;
                var labelWidth = SKPaints.TextStatusSmallEsp.MeasureText(label);
                var spacing = 1f * ESPConfig.FontScale;
                var top = clientArea.Top + spacing;
                var labelHeight = SKPaints.TextStatusSmallEsp.FontSpacing;

                var bgRect = new SKRect(
                    clientArea.Width / 2 - labelWidth / 2,
                    top,
                    clientArea.Width / 2 + labelWidth / 2,
                    top + labelHeight + spacing);

                canvas.DrawRect(bgRect, SKPaints.PaintTransparentBacker);

                var textLoc = new SKPoint(clientArea.Width / 2, top + labelHeight);
                canvas.DrawText(label, textLoc, SKPaints.TextStatusSmallEsp);
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"ERROR Setting ESP Status Text: {ex}");
            }
        }

        /// <summary>
        /// Draw fireport aim in front of player.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawFireportAim(SKCanvas canvas, LocalPlayer localPlayer)
        {
            if (localPlayer.Firearm.FireportPosition is not Vector3 fireportPos)
                return;
            if (localPlayer.Firearm.FireportRotation is not Quaternion fireportRot)
                return;
            if (!CameraManagerBase.WorldToScreen(ref fireportPos, out var fireportPosScr))
                return;
            var forward = fireportRot.Down();
            var targetPos = fireportPos += forward * 1000f;
            if (!CameraManagerBase.WorldToScreen(ref targetPos, out var targetScr))
                return;

            canvas.DrawLine(fireportPosScr, targetScr, SKPaints.PaintBasicESP);
        }

        /// <summary>
        /// Draw player's Magazine/Ammo Count on ESP.
        /// </summary>
        private void DrawMagazine(SKCanvas canvas, LocalPlayer localPlayer)
        {
            var mag = localPlayer.Firearm.Magazine;
            string counter;
            if (mag.IsValid)
                counter = $"{mag.Count} / {mag.MaxCount}";
            else
                counter = "-- / --";

            var textWidth = SKPaints.TextMagazineESP.MeasureText(counter);
            var wepInfo = mag.WeaponInfo;
            if (wepInfo is not null)
                textWidth = Math.Max(textWidth, SKPaints.TextMagazineInfoESP.MeasureText(wepInfo));

            var textHeight = SKPaints.TextMagazineESP.FontSpacing + SKPaints.TextMagazineInfoESP.FontSpacing;
            var x = CameraManagerBase.Viewport.Width - textWidth - 15f * ESPConfig.FontScale;
            var y = CameraManagerBase.Viewport.Height - CameraManagerBase.Viewport.Height * 0.10f - textHeight + 4f * ESPConfig.FontScale;

            if (wepInfo is not null)
                canvas.DrawText(wepInfo, x, y, SKPaints.TextMagazineInfoESP); // Draw Weapon Info

            canvas.DrawText(counter, x,
                y + (SKPaints.TextMagazineESP.FontSpacing - SKPaints.TextMagazineInfoESP.FontSpacing +
                     6f * ESPConfig.FontScale), SKPaints.TextMagazineESP); // Draw Counter
        }

        /// <summary>
        /// Draw Mines/Claymores on ESP.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawMines(SKCanvas canvas, LocalPlayer localPlayer, Memory<Vector3> mines)
        {
            foreach (ref var mine in mines.Span)
            {
                var dist = Vector3.Distance(localPlayer.Position, mine);
                if (dist > MineSettings.RenderDistance)
                    continue;

                if (!CameraManagerBase.WorldToScreen(ref mine, out var scrPos))
                    continue;

                var scale = ESP.Config.FontScale;

                switch (MineSettings.RenderMode)
                {
                    case EntityRenderMode.None:
                        break;

                    case EntityRenderMode.Dot:
                        var dotSize = 3f * scale;
                        canvas.DrawCircle(scrPos.X, scrPos.Y, dotSize, SKPaints.PaintExplosiveESP);
                        break;

                    case EntityRenderMode.Cross:
                        var crossSize = 5f * scale;
                        using (var thickPaint = new SKPaint
                        {
                            Color = SKPaints.PaintExplosiveESP.Color,
                            StrokeWidth = 1.5f * scale,
                            IsAntialias = true,
                            Style = SKPaintStyle.Stroke
                        })
                        {
                            canvas.DrawLine(
                                scrPos.X - crossSize, scrPos.Y - crossSize,
                                scrPos.X + crossSize, scrPos.Y + crossSize,
                                thickPaint);
                            canvas.DrawLine(
                                scrPos.X - crossSize, scrPos.Y + crossSize,
                                scrPos.X + crossSize, scrPos.Y - crossSize,
                                thickPaint);
                        }
                        break;

                    case EntityRenderMode.Plus:
                        var plusSize = 5f * scale;
                        using (var thickPaint = new SKPaint
                        {
                            Color = SKPaints.PaintExplosiveESP.Color,
                            StrokeWidth = 1.5f * scale,
                            IsAntialias = true,
                            Style = SKPaintStyle.Stroke
                        })
                        {
                            canvas.DrawLine(
                                scrPos.X, scrPos.Y - plusSize,
                                scrPos.X, scrPos.Y + plusSize,
                                thickPaint);
                            canvas.DrawLine(
                                scrPos.X - plusSize, scrPos.Y,
                                scrPos.X + plusSize, scrPos.Y,
                                thickPaint);
                        }
                        break;

                    case EntityRenderMode.Square:
                        var boxHalf = 3f * scale;
                        var boxPt = new SKRect(
                            scrPos.X - boxHalf, scrPos.Y - boxHalf,
                            scrPos.X + boxHalf, scrPos.Y + boxHalf);
                        canvas.DrawRect(boxPt, SKPaints.PaintExplosiveESP);
                        break;

                    case EntityRenderMode.Diamond:
                    default:
                        var diamondSize = 3.5f * scale;
                        using (var diamondPath = new SKPath())
                        {
                            diamondPath.MoveTo(scrPos.X, scrPos.Y - diamondSize);
                            diamondPath.LineTo(scrPos.X + diamondSize, scrPos.Y);
                            diamondPath.LineTo(scrPos.X, scrPos.Y + diamondSize);
                            diamondPath.LineTo(scrPos.X - diamondSize, scrPos.Y);
                            diamondPath.Close();
                            canvas.DrawPath(diamondPath, SKPaints.PaintExplosiveESP);
                        }
                        break;
                }

                if (MineSettings.ShowName || MineSettings.ShowDistance)
                {
                    var textY = scrPos.Y + 16f * scale;
                    var textPt = new SKPoint(scrPos.X, textY);
                    var label = MineSettings.ShowName ? "*DANGER* Mine" : null;

                    textPt.DrawESPText(
                        canvas,
                        null,
                        localPlayer,
                        MineSettings.ShowDistance,
                        SKPaints.TextExplosiveESP,
                        label,
                        dist
                    );
                }
            }
        }

        /// <summary>
        /// Draw 'ESP Hidden' notification.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawNotShown(SKCanvas canvas)
        {
            var textPt = new SKPoint(CameraManagerBase.Viewport.Left + 4.5f * ESPConfig.FontScale,
                CameraManagerBase.Viewport.Top + 14f * ESPConfig.FontScale);
            canvas.DrawText("ESP Hidden", textPt, SKPaints.TextBasicESPLeftAligned);
        }

        /// <summary>
        /// Draw FPS Counter.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawFPS(SKCanvas canvas)
        {
            var textPt = new SKPoint(CameraManagerBase.Viewport.Left + 4.5f * ESPConfig.FontScale,
                CameraManagerBase.Viewport.Top + 14f * ESPConfig.FontScale);
            canvas.DrawText($"{_fps}fps", textPt, SKPaints.TextBasicESPLeftAligned);
        }

        /// <summary>
        /// Draw the Aim FOV Circle.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawAimFOV(SKCanvas canvas) =>
            canvas.DrawCircle(CameraManagerBase.ViewportCenter, Aimbot.Config.FOV, SKPaints.PaintBasicESP);

        /// <summary>
        /// Draw all filtered Loot Items within range.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawLoot(SKCanvas canvas, LocalPlayer localPlayer)
        {
            var loot = Loot?.Where(x => x is not QuestItem);
            if (loot is not null)
            {
                foreach (var item in loot)
                {
                    item.DrawESP(canvas, localPlayer);
                }
            }
        }

        /// <summary>
        /// Draw all filtered Loot Items within range.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawContainers(SKCanvas canvas, LocalPlayer localPlayer)
        {
            var containers = Containers;
            if (containers is not null)
            {
                foreach (var container in containers)
                {
                    if (LootSettingsControl.ContainerIsTracked(container.ID ?? "NULL"))
                    {
                        if (Config.Containers.HideSearched && container.Searched)
                            continue;

                        container.DrawESP(canvas, localPlayer);
                    }
                }
            }
        }

        /// <summary>
        /// Draw all Open/Pending exfils.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawExfils(SKCanvas canvas, LocalPlayer localPlayer)
        {
            var exits = Exits;
            if (exits is not null)
                foreach (var exit in exits)
                    exit.DrawESP(canvas, localPlayer);
        }

        /// <summary>
        /// Draw switches for the map.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawSwitches(SKCanvas canvas, LocalPlayer localPlayer)
        {
            if (GameData.Switches.TryGetValue(MapID, out var switches))
            {
                foreach (var switchEntry in switches)
                {
                    var switchObj = new Tarkov.GameWorld.Exits.Switch(switchEntry.Value, switchEntry.Key);
                    switchObj.DrawESP(canvas, localPlayer);
                }
            }
        }

        /// <summary>
        /// Draw all grenades within range.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawExplosives(SKCanvas canvas, LocalPlayer localPlayer)
        {
            var explosives = Explosives;
            if (explosives is not null)
                foreach (var explosive in explosives)
                    explosive.DrawESP(canvas, localPlayer);
        }
        /// <summary>
        /// Draw all grenades within range.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawDoors(SKCanvas canvas, LocalPlayer localPlayer)
        {
            var doors = Memory.Game?.Interactables._Doors;
            if (doors is null || doors.Count == 0) return;
        
            foreach (var door in doors)
            {
                door.DrawESP(canvas, localPlayer);
            }
        }
        /// <summary>
        /// Draw all quest locations within range.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawQuests(SKCanvas canvas, LocalPlayer localPlayer)
        {
            if (LootItem.QuestItemESPSettings.Enabled && !localPlayer.IsScav)
            {
                var questItems = Loot?.Where(x => x is QuestItem);
                if (questItems is not null)
                    foreach (var item in questItems)
                        item.DrawESP(canvas, localPlayer);
            }

            if (QuestManager.ESPSettings.Enabled && !localPlayer.IsScav)
            {
                var questLocations = Memory.QuestManager?.LocationConditions;
                if (questLocations is not null)
                    foreach (var loc in questLocations)
                        loc.DrawESP(canvas, localPlayer);
            }
        }

        /// <summary>
        /// Draw Raid Stats in top right corner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawRaidStats(SKCanvas canvas, IReadOnlyCollection<Player> players)
        {
            var hostiles = players
                .Where(x => x.IsHostileActive)
                .ToArray();

            var pmcCount = hostiles.Count(x => x.IsPmc);
            var pscavCount = hostiles.Count(x => x.Type is Player.PlayerType.PScav);
            var aiCount = hostiles.Count(x => x.IsAI);
            var bossCount = hostiles.Count(x => x.Type is Player.PlayerType.AIBoss);

            var lines = new string[]
            {
                $"PMC: {pmcCount}",
                $"PScav: {pscavCount}",
                $"AI: {aiCount}",
                $"Boss: {bossCount}"
            };

            var x = CameraManagerBase.Viewport.Right - 3f * ESPConfig.FontScale;
            var y = CameraManagerBase.Viewport.Top + SKPaints.TextBasicESPRightAligned.TextSize +
                    CameraManagerBase.Viewport.Height * 0.0575f * ESPConfig.FontScale;

            foreach (var line in lines)
            {
                canvas.DrawText(line, x, y, SKPaints.TextBasicESPRightAligned);
                y += SKPaints.TextBasicESPRightAligned.TextSize;
            }
        }

        /// <summary>
        /// Draw player's Energy/Hydration bars on ESP (bottom left).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawEnergyHydration(SKCanvas canvas, LocalPlayer localPlayer)
        {
            var energy = localPlayer.GetEnergy();
            var hydration = localPlayer.GetHydration();

            var scale = ESPConfig.FontScale;
            var barHeight = 12f * scale;
            var spacing = 6f * scale;
            var margin = 15f * scale;

            var barWidth = 150f * scale;
            var x = CameraManagerBase.Viewport.Width - barWidth - margin;

            var magazineTextHeight = SKPaints.TextMagazineESP.FontSpacing + SKPaints.TextMagazineInfoESP.FontSpacing;
            var magazineY = CameraManagerBase.Viewport.Height - CameraManagerBase.Viewport.Height * 0.10f - magazineTextHeight + 4f * scale;

            var baseY = magazineY - (barHeight * 2 + spacing * 3 + 8f * scale);
            var energyY = baseY;
            var hydrationY = energyY + barHeight + spacing;

            DrawStatusBar(canvas, x, energyY, barWidth, barHeight, energy, 100f, SKColor.Parse("#D4C48A"), SKColor.Parse("#B8A765"));
            DrawStatusBar(canvas, x, hydrationY, barWidth, barHeight, hydration, 100f, SKColor.Parse("#5B9BD5"), SKColor.Parse("#4682B4"));

            DrawCenteredBarText(canvas, x, energyY, barWidth, barHeight, energy.ToString("F1"), scale);
            DrawCenteredBarText(canvas, x, hydrationY, barWidth, barHeight, hydration.ToString("F1"), scale);
        }

        /// <summary>
        /// Helper method to draw a status bar with background, fill, and optional label.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawStatusBar(SKCanvas canvas, float x, float y, float width, float height, float current, float max, SKColor fillColor, SKColor borderColor, string label = null)
        {
            var scale = ESPConfig.FontScale;

            var bgRect = new SKRect(x, y, x + width, y + height);
            using var bgPaint = new SKPaint
            {
                Color = SKColors.Black.WithAlpha(180),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRect(bgRect, bgPaint);

            var percentage = Math.Max(0f, Math.Min(1f, current / max));
            var fillWidth = width * percentage;
            var fillRect = new SKRect(x, y, x + fillWidth, y + height);

            using var fillPaint = new SKPaint
            {
                Color = fillColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRect(fillRect, fillPaint);

            using var borderPaint = new SKPaint
            {
                Color = borderColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1f * scale,
                IsAntialias = true
            };
            canvas.DrawRect(bgRect, borderPaint);
        }

        /// <summary>
        /// Helper method to draw centered text inside a bar.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawCenteredBarText(SKCanvas canvas, float barX, float barY, float barWidth, float barHeight, string text, float scale)
        {
            if (string.IsNullOrEmpty(text)) return;

            var textSize = 11f * scale;

            using var textPaint = new SKPaint
            {
                Color = SKColors.White,
                TextSize = textSize,
                IsAntialias = true,
                Typeface = SKTypeface.Default
            };

            using var outlinePaint = new SKPaint
            {
                Color = SKColors.Black,
                TextSize = textSize,
                IsAntialias = true,
                Typeface = SKTypeface.Default,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f * scale
            };

            var textWidth = textPaint.MeasureText(text);
            var centerX = barX + (barWidth / 2f) - (textWidth / 2f);
            var centerY = barY + (barHeight / 2f) + (textSize / 3f);

            canvas.DrawText(text, centerX, centerY, outlinePaint);
            canvas.DrawText(text, centerX, centerY, textPaint);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F11)
            {
                SetFullscreen(!IsFullscreen);
                return true;
            }

            if (keyData == Keys.Escape && IsFullscreen)
            {
                SetFullscreen(false);
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            if (WindowState is FormWindowState.Maximized)
                SetFullscreen(true);
            else
                base.OnSizeChanged(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                CameraManagerBase.EspRunning = false;
                Window = null;
                _renderTimer.Dispose();

                // Clean up object pools
                foreach (var path in _pathPool)
                    path.Dispose();

                _pathPool.Clear();
            }
            finally
            {
                base.OnFormClosing(e);
            }
        }

        #endregion
    }
}