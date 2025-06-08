global using eft_dma_shared;
global using eft_dma_shared.Common;
global using eft_dma_shared.Misc;
global using SDK;
global using SkiaSharp;
global using SkiaSharp.Views.Desktop;
global using System.Buffers;
global using System.Buffers.Binary;
global using System.Collections;
global using System.Collections.Concurrent;
global using System.ComponentModel;
global using System.Data;
global using System.Diagnostics;
global using System.Net;
global using System.Net.Http.Headers;
global using System.Net.Security;
global using System.Numerics;
global using System.Reflection;
global using System.Runtime.CompilerServices;
global using System.Runtime.InteropServices;
global using System.Security.Cryptography;
global using System.Security.Cryptography.X509Certificates;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using SKSvg = Svg.Skia.SKSvg;
using eft_dma_radar;
using eft_dma_radar.Tarkov;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.Features.MemoryWrites.Patches;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.GameWorld.Exits;
using eft_dma_radar.Tarkov.GameWorld.Explosives;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using eft_dma_shared.Common.DMA.ScatterAPI;
using eft_dma_shared.Common.Features;
using eft_dma_shared.Common.Maps;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.Data;
using eft_dma_shared.Common.UI;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using Vmmsharp;
using Application = System.Windows.Forms.Application;
using MessageBox = HandyControl.Controls.MessageBox;

[assembly: AssemblyTitle(Program.Name)]
[assembly: AssemblyProduct(Program.Name)]
[assembly: AssemblyCopyright("BSD Zero Clause License ©2025 lone-dma")]
[assembly: SupportedOSPlatform("Windows")]

namespace eft_dma_radar
{
    internal static class Program
    {
        internal const string Name = "EFT DMA Radar";
        internal const string Version = "1.1.3";

        /// <summary>
        /// Current application mode
        /// </summary>
        public static ApplicationMode CurrentMode { get; private set; } = ApplicationMode.Normal;

        /// <summary>
        /// Global Program Configuration.
        /// </summary>
        public static Config Config { get; private set; }

        /// <summary>
        /// Path to the Configuration Folder in %AppData%
        /// </summary>
        public static DirectoryInfo ConfigPath { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "eft-dma-radar"));

        /// <summary>
        /// Update the global configuration reference (used for config imports)
        /// </summary>
        /// <param name="newConfig">The new configuration to use</param>
        public static void UpdateConfig(Config newConfig)
        {
            if (newConfig == null)
                throw new ArgumentNullException(nameof(newConfig));

            Config = newConfig;
            SharedProgram.UpdateConfig(Config);

            LoneLogging.WriteLine("[Program] Global config reference updated");
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static public void Main()
        {
            InitializeDpiAwareness();

            var app = new App();
            app.InitializeComponent();

            try
            {
                StartApplication(app, ApplicationMode.Normal);
            }
            catch (Exception ex)
            {
                HandleStartupException(app, ex);
            }
        }

        #region Private Members

        static Program()
        {
            try
            {
                ConfigPath.Create();
                var config = Config.Load();
                SharedProgram.Initialize(ConfigPath, config);
                Config = config;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                throw;
            }
        }

        /// <summary>
        /// Start the application in the specified mode
        /// </summary>
        private static void StartApplication(App app, ApplicationMode mode)
        {
            CurrentMode = mode;

            var mainWindow = new MainWindow();
            app.MainWindow = mainWindow;

            if (mode == ApplicationMode.Normal)
                ConfigureProgram();
            else
                ConfigureSafeMode();

            mainWindow.InitializeComponent();
            mainWindow.Show();
            mainWindow.Activate();
            app.Run();
        }

        /// <summary>
        /// Handle startup exceptions and offer recovery options
        /// </summary>
        private static void HandleStartupException(App app, Exception ex)
        {
            var errorMessage = ex.ToString();

            if (errorMessage.Contains("DMA Initialization Failed!"))
            {
                errorMessage += "\n\nWould you like to continue in Safe Mode? (UI and Config only)";

                var result = MessageBox.Show(errorMessage, "Continue in Safe Mode?", MessageBoxButton.YesNo, MessageBoxImage.Error);

                if (result == MessageBoxResult.Yes)
                    StartApplication(app, ApplicationMode.SafeMode);
                else
                    Environment.Exit(1);
            }
            else
            {
                var result = MessageBox.Show(
                    $"Startup Error: {ex.Message}\n\nWould you like to start in Safe Mode?",
                    "Startup Error",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);

                if (result == MessageBoxResult.Yes)
                    StartApplication(app, ApplicationMode.SafeMode);
                else
                    throw new Exception($"Application startup failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Configure the program for safe mode (no DMA functionality)
        /// </summary>
        private static void ConfigureSafeMode()
        {
            var loading = LoadingWindow.Create();

            try
            {
                loading.UpdateStatus("Starting in Safe Mode...", 10);
                LoneLogging.WriteLine("Starting application in Safe Mode - DMA functionality disabled");

                loading.UpdateStatus("Loading Configuration...", 25);

                loading.UpdateStatus("Initializing Safe Memory Interface...", 40);
                MemoryInterface.ModuleInit();

                loading.UpdateStatus("Loading Safe UI Components...", 50);
                try
                {
                    loading.UpdateStatus("Loading Tarkov.Dev Data...", 60);
                    EftDataManager.ModuleInitAsync(loading).GetAwaiter().GetResult();

                    loading.UpdateStatus("Caching Item Icons...", 70);
                    CacheAllItemIcons();
                }
                catch (Exception ex)
                {
                    LoneLogging.WriteLine($"Non-critical safe mode component failed: {ex.Message}");
                }

                loading.UpdateStatus("Initializing Safe Mode Features...", 85);
                InitializeSafeModeFeatures();

                loading.UpdateStatus("Safe Mode Ready - DMA functions disabled", 100);
                Thread.Sleep(500);
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"Safe Mode initialization failed: {ex}");
                loading.UpdateStatus("Safe Mode initialization failed", 100);
                Thread.Sleep(1000);
            }
            finally
            {
                loading.Dispatcher.Invoke(() => loading.Close());
            }
        }

        /// <summary>
        /// Initialize features that are safe to use without DMA
        /// </summary>
        private static void InitializeSafeModeFeatures()
        {
            try
            {
                LoneLogging.WriteLine("Safe mode features initialized");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"Error initializing safe mode features: {ex}");
            }
        }

        /// <summary>
        /// Configure Program Startup (Normal Mode).
        /// </summary>
        private static void ConfigureProgram()
        {
            var loading = LoadingWindow.Create();

            try
            {
                loading.UpdateStatus("Loading Tarkov.Dev Data...", 15);
                EftDataManager.ModuleInitAsync(loading).GetAwaiter().GetResult();

                loading.UpdateStatus("Caching Item Icons...", 25);
                CacheAllItemIcons();

                loading.UpdateStatus("Loading Map Assets...", 35);
                LoneMapManager.ModuleInit();

                loading.UpdateStatus("Starting DMA Connection...", 50);
                MemoryInterface.ModuleInit();

                loading.UpdateStatus("Loading Remaining Modules...", 75);
                FeatureManager.ModuleInit();

                ResourceJanitor.ModuleInit(new Action(CleanupWindowResources));
                RuntimeHelpers.RunClassConstructor(typeof(MemPatchFeature<FixWildSpawnType>).TypeHandle);

                loading.UpdateStatus("Loading Completed!", 100);
                Thread.Sleep(300);
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                loading.Dispatcher.Invoke(() => loading.Close());
            }
        }

        private static void CacheAllItemIcons()
        {
            string iconCachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "eft-dma-radar", "Assets", "Icons", "Items");

            Directory.CreateDirectory(iconCachePath);
            
            // Cache item icons
            Parallel.ForEach(EftDataManager.AllItems.Keys, itemId =>
            {
                try
                {
                    string pngPath = Path.Combine(iconCachePath, $"{itemId}.png");
                    if (File.Exists(pngPath) && new FileInfo(pngPath).Length > 1024) return;

                    LoneLogging.WriteLine($"[IconCache] Caching item icon: {itemId}");
                    eft_dma_radar.Converters.ItemIconConverter.SaveItemIconAsPng(itemId, iconCachePath).Wait();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[IconCache] Failed to cache item {itemId}: {ex}");
                }
            });
        }

        private static void CleanupWindowResources()
        {
            MainWindow.Window?.PurgeSKResources();
            ESPForm.Window?.PurgeSKResources();
        }

        /// <summary>
        /// Initialize DPI awareness for the application
        /// </summary>
        private static void InitializeDpiAwareness()
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

                LoneLogging.WriteLine("[DPI] Successfully enabled PerMonitorV2 DPI awareness");
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[DPI] Failed to set DPI awareness: {ex.Message}");

                try
                {
                    Application.SetHighDpiMode(HighDpiMode.SystemAware);
                    LoneLogging.WriteLine("[DPI] Fallback: Enabled SystemAware DPI awareness");
                }
                catch
                {
                    LoneLogging.WriteLine("[DPI] Warning: Could not enable DPI awareness");
                }
            }
        }

        #endregion
    }
}