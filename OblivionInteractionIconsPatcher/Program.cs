using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CommandLine;
using Noggog;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;

namespace OblivionInteractionIconsPatcher
{
    /// <summary>
    /// Main patcher logic for generating interaction icon JSON files for Skyrim mods.
    /// </summary>
    partial class Program
    {
        public class Options
        {
            // [Option('m', "mo2", Required = false, Default = false, HelpText = "Using Mod Organizer 2 Virtual File System.")]
            // public bool ModOrganizer2 { get; set; }
            [Option('s', "single", Required = false, HelpText = "Path to individual mod directory.")]
            public string? Single { get; set; }
            [Option('o', "override", Required = false, Default = false, HelpText = "Override existing configuration files (not recommended).")]
            public bool Override { get; set; }
        }

        /// <summary>
        /// Regex to extract color hex code from a string.
        /// </summary>
        [System.Text.RegularExpressions.GeneratedRegex(@"color\s*=\s*['""]#([0-9A-Fa-f]{6})['""]")]
        private static partial System.Text.RegularExpressions.Regex MyRegex();

        /// <summary>
        /// List of official Bethesda plugin ModKeys to exclude from patching.
        /// </summary>
        private static readonly ModKey[] BethesdaPlugins =
        [
            Skyrim.ModKey, Update.ModKey, Dawnguard.ModKey,
            Dragonborn.ModKey, HearthFires.ModKey
        ];

        /// <summary>
        /// Set of Creation Club plugin ModKeys to exclude from patching.
        /// </summary>
        private static readonly HashSet<ModKey> CreationClubPlugins =
            GetCreationClubPlugins(GameEnvironment.Typical.Skyrim(SkyrimRelease.SkyrimSE).CreationClubListingsFilePath ?? string.Empty);

        /// <summary>
        /// Reads Creation Club plugin ModKeys from the listings file.
        /// </summary>
        private static HashSet<ModKey> GetCreationClubPlugins(FilePath path)
        {
            if (!File.Exists(path)) return [];
            try
            {
                return [.. File.ReadAllLines(path)
                    .Select(line => ModKey.TryFromFileName(new FileName(line)))
                    .Where(plugin => plugin.HasValue)
                    .Select(plugin => plugin!.Value)];
            }
            catch
            {
                return [];
            }
        }

        /// <summary>
        /// Determines if a mod should be processed (not official, not CC, not skymoji, and has flora or activators).
        /// </summary>
        private static bool PluginFilter(ISkyrimModGetter? mod) =>
            mod is not null &&
            !BethesdaPlugins.Contains(mod.ModKey) &&
            !CreationClubPlugins.Contains(mod.ModKey) &&
            !mod.ModKey.Name.Contains("skymoji", StringComparison.OrdinalIgnoreCase) &&
            !mod.ModKey.Name.Equals(["3DNPC", "3DNPC0", "3DNPC1"], StringComparison.OrdinalIgnoreCase) &&
            (mod.Florae.Count > 0 || mod.Activators.Count > 0);

        /// <summary>
        /// Determines whether a plugin's output directory should be (re)generated.
        /// Returns true if the directory does not exist or does not contain any "acti" or "flora" files,
        /// meaning patching should proceed. Returns false if such files already exist, indicating patching can be skipped.
        /// </summary>
        /// <param name="mod">The mod/plugin to check for existing output.</param>
        /// <param name="path">The root path to the DynamicStringDistributor output directory.</param>
        /// <returns>True if patching should proceed; false if output already exists.</returns>
        private static bool OverrideFilter(ISkyrimModGetter mod, string path)
        {
            var modDir = Path.Combine(path, mod.ModKey.FileName);
            if (!Directory.Exists(modDir)) return true;
            return !Directory.EnumerateFiles(modDir)
                .Any(f => f.Contains("acti") || f.Contains("flora"));
        }

        /// <summary>
        /// Formats a FormKey as a string for JSON output.
        /// </summary>
        private static string PackageFormKey(FormKey formKey) =>
            $"{formKey.IDString()}|{formKey.ModKey.FileName}";

        /// <summary>
        /// Formats an icon string with optional color for JSON output.
        /// </summary>
        private static string PackageIcon(string iconCharacter, string? iconColor) =>
            !iconColor.IsNullOrEmpty()
                ? $"<font color='#{iconColor}'><font face='$Iconographia'> {iconCharacter} </font></font>"
                : $"<font face='$Iconographia'> {iconCharacter} </font>";

        // private static (ModKey, List<Record>?, List<Record>?) ProccessPlugin(ISkyrimModGetter plugin, IGameEnvironment<ISkyrimMod, ISkyrimModGetter> env, string iconColor)
        // {
        //     var florae = ProcessFlora(plugin, env, iconColor);
        //     var activators = ProcessActivators(plugin, env, iconColor);
        //     return (plugin.ModKey, florae, activators);
        // }

        /// <summary>
        /// Main entry point. Processes all enabled plugins and writes flora/activator icon JSON files.
        /// </summary>
        public static void Main(string[] args)
        {
            //string rootDirectory;
            Parser.Default.ParseArguments<Options>(args).WithParsed(o => { });

            // Set up Skyrim SE environment and output directory
            var env = GameEnvironment.Typical.Skyrim(SkyrimRelease.SkyrimSE);
            var dataPath = env.DataFolderPath.Path;
            var dsdPath = Path.Combine(dataPath, "SKSE\\Plugins\\DynamicStringDistributor");

            // Try to extract icon color from skymoji JSON if present
            string? iconColor = null;
            var skymojiPath = Path.Combine(dsdPath, "skyrim.esm", "skymojiactivators10.json");
            if (File.Exists(skymojiPath))
            {
                try
                {
                    var data = JsonSerializer.Deserialize<List<Record>>(File.ReadAllText(skymojiPath));
                    if (data?.Count > 0)
                    {
                        var match = MyRegex().Match(data.First().@string);
                        if (match.Success)
                            iconColor = match.Groups[1].Value;
                        if (!iconColor.IsNullOrEmpty())
                            Console.WriteLine($"Found color override {iconColor}");
                    }
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"Error deserializing JSON: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An unexpected error occurred: {ex.Message}");
                }
            }

            // Get all enabled plugins that pass the filter
            var plugins = env.LoadOrder.ListedOrder.OnlyEnabled().Select(m => m.Mod).Where(PluginFilter).ToList();
            plugins = [.. plugins.Where(p => p != null && OverrideFilter(p, dsdPath))];

            // Set up JSON serialization options
            var serializeOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            Console.WriteLine($"Patching plugin(s)...");
            var patched = 0;

            // Process each plugin
            foreach (var plugin in plugins)
            {
                if (plugin is null) continue;

                // Generate flora and activator records
                var florae = ProcessFlora(plugin, env, iconColor);
                var activators = ProcessActivators(plugin, env, iconColor);

                // Write JSON files if there are any records
                var jsonDirectory = Path.Combine(dsdPath, plugin.ModKey.FileName);
                if (florae.Count > 0 || activators.Count > 0)
                {
                    Console.WriteLine(plugin.ModKey.FileName);
                    var test = Directory.CreateDirectory(jsonDirectory);
                    if (florae.Count > 0)
                        File.WriteAllText(Path.Combine(jsonDirectory, $"skymoji_{plugin.ModKey.Name.ToLower()}FLOR.json"),
                            JsonSerializer.Serialize(florae, serializeOptions));
                    if (activators.Count > 0)
                        File.WriteAllText(Path.Combine(jsonDirectory, $"skymoji_{plugin.ModKey.Name.ToLower()}ACTI.json"),
                            JsonSerializer.Serialize(activators, serializeOptions));
                    patched++;
                }
            }
            Console.WriteLine($"Succesfully patched {patched} plugin(s)");
        }

        /// <summary>
        /// Processes all flora records in a plugin and returns a list of output records.
        /// </summary>
        private static List<Record> ProcessFlora(ISkyrimModGetter plugin, IGameEnvironment<ISkyrimMod, ISkyrimModGetter> env, string? iconColor)
        {
            var florae = new List<Record>();
            foreach (var flora in plugin.Florae)
            {
                // Skip if flora is unchanged from its origin
                if (flora.FormKey.ModKey != plugin.ModKey)
                {
                    var origin = flora.FormKey.ToLink<IFloraGetter>().ResolveAll(env.LinkCache).Last();
                    if (flora.ActivateTextOverride == origin.ActivateTextOverride && flora.Name == origin.Name) continue;
                }

                string iconCharacter = GetFloraIcon(flora);

                var record = new Record(
                    PackageFormKey(flora.FormKey),
                    "FLOR RNAM",
                    PackageIcon(iconCharacter, iconColor)
                );
                florae.Add(record);
            }
            return florae;
        }

        /// <summary>
        /// Determines the icon character for a flora record.
        /// </summary>
        private static string GetFloraIcon(IFloraGetter flora)
        {
            var full = flora.Name?.String;
            var rnam = flora.ActivateTextOverride?.String;

            //Mushrooms
            if (flora.HarvestSound.FormKey.Equals(Skyrim.SoundDescriptor.ITMIngredientMushroomUp.FormKey)
                || full.Contains(["spore", "cap", "crown", "shroom"], StringComparison.OrdinalIgnoreCase))
                return "A";
            //Clams
            if (flora.HarvestSound.FormKey.Equals(Skyrim.SoundDescriptor.ITMIngredientClamUp.FormKey)
                || full.ContainsNullable("clam", StringComparison.OrdinalIgnoreCase))
                return "b";
            //Fill | Cask or Barrel (Fill)
            if (flora.HarvestSound.FormKey.Equals(Skyrim.SoundDescriptor.ITMPotionUpSD.FormKey)
                || rnam.ContainsNullable("fill bottles", StringComparison.OrdinalIgnoreCase)
                || full.Contains(["barrel", "cask"], StringComparison.OrdinalIgnoreCase))
                return "L";
            //Coin Pouch
            if (flora.HarvestSound.FormKey.Equals(Skyrim.SoundDescriptor.ITMCoinPouchUp.FormKey)
                || flora.HarvestSound.FormKey.Equals(Skyrim.SoundDescriptor.ITMCoinPouchDown.FormKey)
                || full.ContainsNullable("coin purse", StringComparison.OrdinalIgnoreCase))
                return "S";
            //Catch, Scavenge
            if (rnam.Equals(["catch", "scavenge"], StringComparison.OrdinalIgnoreCase))
                return "S";
            //Default
            return "Q";
        }

        /// <summary>
        /// Processes all activator records in a plugin and returns a list of output records.
        /// </summary>
        private static List<Record> ProcessActivators(ISkyrimModGetter plugin, IGameEnvironment<ISkyrimMod, ISkyrimModGetter> env, string? iconColor)
        {
            var activators = new List<Record>();
            foreach (var activator in plugin.Activators)
            {
                // Skip if activator is unchanged from its origin
                if (activator.FormKey.ModKey != plugin.ModKey)
                {
                    var origin = activator.FormKey.ToLink<IActivatorGetter>().ResolveAll(env.LinkCache).Last();
                    if (activator.ActivateTextOverride?.String == origin.ActivateTextOverride?.String && activator.Name?.String == origin.Name?.String) continue;
                }

                var (iconCharacter, colorOverride) = GetActivatorIconAndColor(activator);
                if (iconCharacter.IsNullOrEmpty()) continue;
                var record = new Record(
                    PackageFormKey(activator.FormKey),
                    "ACTI RNAM",
                    PackageIcon(iconCharacter, colorOverride ?? iconColor)
                );
                activators.Add(record);
            }
            return activators;
        }

        /// <summary>
        /// Determines the icon character and color override for an activator record.
        /// </summary>
        private static (string icon, string? colorOverride) GetActivatorIconAndColor(IActivatorGetter activator)
        {
            var full = activator.Name?.String;
            var edid = activator.EditorID;
            var rnam = activator.ActivateTextOverride?.String;

            // Blacklisting superfluous entries
            if (activator.ActivateTextOverride == null && edid.Contains(["trig", "fx"], StringComparison.OrdinalIgnoreCase))
                return (string.Empty, null);
            //Steal
            if (rnam.EqualsNullable("steal", StringComparison.OrdinalIgnoreCase))
                return ("S", "#ff0000");
            //Pickpocket
            if (rnam.EqualsNullable("pickpocket", StringComparison.OrdinalIgnoreCase))
                return ("b", "#ff0000");
            //Steal From
            if (rnam.EqualsNullable("steal from", StringComparison.OrdinalIgnoreCase))
                return ("V", "#ff0000");
            //Close
            if (rnam.EqualsNullable("close", StringComparison.OrdinalIgnoreCase))
                return ("X", "#dddddd");
            //Chest, Search, Open Chest
            if (full.EqualsNullable("chest", StringComparison.OrdinalIgnoreCase)
                || rnam.EqualsNullable("search", StringComparison.OrdinalIgnoreCase)
                || (full.ContainsNullable("chest", StringComparison.OrdinalIgnoreCase) && rnam.EqualsNullable("open", StringComparison.OrdinalIgnoreCase)))
                return ("V", null);
            //Grab, Touch
            if (rnam.Equals(["grab", "touch"], StringComparison.OrdinalIgnoreCase))
                return ("S", null);
            //Levers
            if ((activator.Keywords != null && activator.Keywords.Contains(Skyrim.Keyword.ActivatorLever.FormKey))
                || full.ContainsNullable("lever", StringComparison.OrdinalIgnoreCase)
                || edid.ContainsNullable("pullbar", StringComparison.OrdinalIgnoreCase))
                return ("D", null);
            //Chain
            if (full.ContainsNullable("chain", StringComparison.OrdinalIgnoreCase))
                return ("E", null);
            //Mine
            if (rnam.EqualsNullable("mine", StringComparison.OrdinalIgnoreCase))
                return ("G", null);
            //Button, Press, Examine, Push, Investigate
            if (full.ContainsNullable("button", StringComparison.OrdinalIgnoreCase)
                || rnam.Equals(["press", "examine", "push", "investigate"], StringComparison.OrdinalIgnoreCase))
                return ("F", null);
            //Business Ledger, Write
            if (full.ContainsNullable("ledger", StringComparison.OrdinalIgnoreCase)
                || rnam.EqualsNullable("write", StringComparison.OrdinalIgnoreCase))
                return ("H", null);
            //Pray
            if (full.Contains(["shrine", "altar"], StringComparison.OrdinalIgnoreCase)
                || edid.ContainsNullable("dlc2standingstone", StringComparison.OrdinalIgnoreCase)
                || rnam.Equals(["pray", "worship"], StringComparison.OrdinalIgnoreCase))
                return ("C", null);
            //Drink
            if (rnam.EqualsNullable("drink", StringComparison.OrdinalIgnoreCase))
                return ("J", null);
            //Eat
            if (rnam.EqualsNullable("eat", StringComparison.OrdinalIgnoreCase))
                return ("K", null);
            //Drop, Place, Exchange
            if (rnam.Equals(["drop", "place", "exchange"], StringComparison.OrdinalIgnoreCase))
                return ("N", null);
            //Pick Up
            if (rnam.EqualsNullable("pick up", StringComparison.OrdinalIgnoreCase))
                return ("O", null);
            //Read
            if (rnam.EqualsNullable("read", StringComparison.OrdinalIgnoreCase))
                return ("P", null);
            //Harvest
            if (rnam.EqualsNullable("harvest", StringComparison.OrdinalIgnoreCase))
                return ("Q", null);
            //Take or Catch
            if (rnam.Equals(["take", "catch"], StringComparison.OrdinalIgnoreCase))
                return ("S", null);
            //Talk, Speak
            if (rnam.Equals(["talk", "speak"], StringComparison.OrdinalIgnoreCase))
                return ("T", null);
            //Sit
            if (rnam.EqualsNullable("sit", StringComparison.OrdinalIgnoreCase))
                return ("U", null);
            //Open Door
            if (rnam.EqualsNullable("open", StringComparison.OrdinalIgnoreCase))
                return ("X", null);
            //Activate
            if (rnam.EqualsNullable("activate", StringComparison.OrdinalIgnoreCase))
                return ("Y", null);
            //Unlock
            if (rnam.EqualsNullable("unlock", StringComparison.OrdinalIgnoreCase))
                return ("Z", null);
            //Sleep
            if (rnam.EqualsNullable("sleep", StringComparison.OrdinalIgnoreCase)
                || full.Contains(["bed", "hammock", "coffin"], StringComparison.OrdinalIgnoreCase))
                return ("a", null);
            //Torch, Sconce
            if (edid.ContainsNullable("sconce", StringComparison.OrdinalIgnoreCase))
                return ("i", null);
            //Dragon Claw
            if (full.ContainsNullable("keyhole", StringComparison.OrdinalIgnoreCase))
                return ("j", null);
            //Civil War Map & Map Marker (Flags)
            if (edid.ContainsNullable("cwmap", StringComparison.OrdinalIgnoreCase))
                return ("F", null);
            //EVG Ladder | Float, Climb
            if (edid.ContainsNullable("ladder", StringComparison.OrdinalIgnoreCase)
                || rnam.Equals(["float", "climb"], StringComparison.OrdinalIgnoreCase))
                return ("d", null);
            //EVG Squeeze
            if (edid.ContainsNullable("squeeze", StringComparison.OrdinalIgnoreCase))
                return ("e", null);
            //CC Fishing
            if (full.ContainsNullable("fishing supplies", StringComparison.OrdinalIgnoreCase))
                return ("I", null);
            //Default
            return ("W", null);
        }
    }
}
