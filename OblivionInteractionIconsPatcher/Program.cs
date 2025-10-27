using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Noggog;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;

namespace OblivionInteractionIconsPatcher
{
    partial class Program
    {
        [System.Text.RegularExpressions.GeneratedRegex(@"color\s*=\s*['""]#([0-9A-Fa-f]{6})['""]")]
        private static partial System.Text.RegularExpressions.Regex MyRegex();

        private static readonly ModKey[] BethesdaPlugins =
        [
            Skyrim.ModKey, Update.ModKey, Dawnguard.ModKey,
            Dragonborn.ModKey, HearthFires.ModKey
        ];

        private static readonly HashSet<ModKey> CreationClubPlugins =
            GetCreationClubPlugins(GameEnvironment.Typical.Skyrim(SkyrimRelease.SkyrimSE).CreationClubListingsFilePath ?? string.Empty);

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

        private static bool PluginFilter(ISkyrimModGetter? mod) =>
            mod is not null &&
            !BethesdaPlugins.Contains(mod.ModKey) &&
            !CreationClubPlugins.Contains(mod.ModKey) &&
            !mod.ModKey.Name.Contains("skymoji") &&
            (mod.Florae.Count > 0 || mod.Activators.Count > 0);

        private static string PackageFormKey(FormKey formKey) =>
            $"{formKey.IDString()}|{formKey.ModKey.FileName}";

        private static string PackageIcon(string iconCharacter, string? iconColor) =>
            !iconColor.IsNullOrEmpty()
                ? $"<font color='{iconColor}'><font face='$Iconographia'> {iconCharacter} </font></font>"
                : $"<font face='$Iconographia'> {iconCharacter} </font>";

        public static void Main(string[] args)
        {
            var env = GameEnvironment.Typical.Skyrim(SkyrimRelease.SkyrimSE);
            var dataPath = env.DataFolderPath.Path;
            var dsdPath = Path.Combine(dataPath, "SKSE\\Plugins\\DynamicStringDistributor1");
            Directory.CreateDirectory(dsdPath);

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

            var plugins = env.LoadOrder.ListedOrder.OnlyEnabled().Select(m => m.Mod).Where(PluginFilter).ToList();

            var serializeOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            foreach (var plugin in plugins)
            {
                if (plugin is null) continue;

                var florae = ProcessFlora(plugin, env, iconColor);
                var activators = ProcessActivators(plugin, env, iconColor);

                if (florae.Count > 0 || activators.Count > 0)
                {
                    Console.WriteLine(plugin.ModKey.FileName);
                    var jsonDirectory = Path.Combine(dsdPath, plugin.ModKey.FileName);
                    Directory.CreateDirectory(jsonDirectory);

                    if (florae.Count > 0)
                        File.WriteAllText(Path.Combine(jsonDirectory, $"{plugin.ModKey.Name.ToLower()}flora.json"),
                            JsonSerializer.Serialize(florae, serializeOptions));
                    if (activators.Count > 0)
                        File.WriteAllText(Path.Combine(jsonDirectory, $"{plugin.ModKey.Name.ToLower()}acti.json"),
                            JsonSerializer.Serialize(activators, serializeOptions));
                }
            }
        }

        private static List<Record> ProcessFlora(ISkyrimModGetter plugin, IGameEnvironment<ISkyrimMod, ISkyrimModGetter> env, string? iconColor)
        {
            var florae = new List<Record>();
            foreach (var flora in plugin.Florae)
            {
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

        private static string GetFloraIcon(IFloraGetter flora)
        {
            var full = flora.Name?.String;
            var rnam = flora.ActivateTextOverride?.String;

            if (flora.HarvestSound.FormKey.Equals(Skyrim.SoundDescriptor.ITMIngredientMushroomUp.FormKey)
                || full.Contains(["spore", "cap", "crown", "shroom"], StringComparison.OrdinalIgnoreCase))
                return "A";
            if (flora.HarvestSound.FormKey.Equals(Skyrim.SoundDescriptor.ITMIngredientClamUp.FormKey)
                || full.ContainsNullable("clam", StringComparison.OrdinalIgnoreCase))
                return "b";
            if (flora.HarvestSound.FormKey.Equals(Skyrim.SoundDescriptor.ITMPotionUpSD.FormKey)
                || rnam.ContainsNullable("fill bottles", StringComparison.OrdinalIgnoreCase)
                || full.Contains(["barrel", "cask"], StringComparison.OrdinalIgnoreCase))
                return "L";
            if (flora.HarvestSound.FormKey.Equals(Skyrim.SoundDescriptor.ITMCoinPouchUp.FormKey)
                || flora.HarvestSound.FormKey.Equals(Skyrim.SoundDescriptor.ITMCoinPouchDown.FormKey)
                || full.ContainsNullable("coin purse", StringComparison.OrdinalIgnoreCase))
                return "S";
            if (rnam.Equals(["catch", "scavenge"], StringComparison.OrdinalIgnoreCase))
                return "S";
            return "Q";
        }

        private static List<Record> ProcessActivators(ISkyrimModGetter plugin, IGameEnvironment<ISkyrimMod, ISkyrimModGetter> env, string? iconColor)
        {
            var activators = new List<Record>();
            foreach (var activator in plugin.Activators)
            {
                if (activator.FormKey.ModKey != plugin.ModKey)
                {
                    var origin = activator.FormKey.ToLink<IActivatorGetter>().ResolveAll(env.LinkCache).Last();
                    if (activator.ActivateTextOverride?.String == origin.ActivateTextOverride?.String && activator.Name?.String == origin.Name?.String) continue;
                }

                var (iconCharacter, colorOverride) = GetActivatorIconAndColor(activator);
                var record = new Record(
                    PackageFormKey(activator.FormKey),
                    "ACTI RNAM",
                    PackageIcon(iconCharacter, colorOverride ?? iconColor)
                );
                activators.Add(record);
            }
            return activators;
        }

        private static (string icon, string? colorOverride) GetActivatorIconAndColor(IActivatorGetter activator)
        {
            var full = activator.Name?.String;
            var edid = activator.EditorID;
            var rnam = activator.ActivateTextOverride?.String;

            // Exclude superfluous entries
            if (activator.ActivateTextOverride == null && edid.Contains(["trigger", "fx"], StringComparison.OrdinalIgnoreCase))
                return (string.Empty, null);

            if (rnam.EqualsNullable("steal", StringComparison.OrdinalIgnoreCase))
                return ("S", "#ff0000");
            if (rnam.EqualsNullable("pickpocket", StringComparison.OrdinalIgnoreCase))
                return ("b", "#ff0000");
            if (rnam.EqualsNullable("steal from", StringComparison.OrdinalIgnoreCase))
                return ("V", "#ff0000");
            if (rnam.EqualsNullable("close", StringComparison.OrdinalIgnoreCase))
                return ("X", "#dddddd");
            if (full.EqualsNullable("chest", StringComparison.OrdinalIgnoreCase)
                || rnam.EqualsNullable("search", StringComparison.OrdinalIgnoreCase)
                || (full.ContainsNullable("chest", StringComparison.OrdinalIgnoreCase) && rnam.EqualsNullable("open", StringComparison.OrdinalIgnoreCase)))
                return ("V", null);
            if (rnam.Equals(["grab", "touch"], StringComparison.OrdinalIgnoreCase))
                return ("S", null);
            if ((activator.Keywords != null && activator.Keywords.Contains(Skyrim.Keyword.ActivatorLever.FormKey))
                || full.ContainsNullable("lever", StringComparison.OrdinalIgnoreCase)
                || edid.ContainsNullable("pullbar", StringComparison.OrdinalIgnoreCase))
                return ("D", null);
            if (full.ContainsNullable("chain", StringComparison.OrdinalIgnoreCase))
                return ("E", null);
            if (rnam.EqualsNullable("mine", StringComparison.OrdinalIgnoreCase))
                return ("G", null);
            if (full.ContainsNullable("button", StringComparison.OrdinalIgnoreCase)
                || rnam.Equals(["press", "examine", "push", "investigate"], StringComparison.OrdinalIgnoreCase))
                return ("F", null);
            if (full.ContainsNullable("ledger", StringComparison.OrdinalIgnoreCase)
                || rnam.EqualsNullable("write", StringComparison.OrdinalIgnoreCase))
                return ("H", null);
            if (full.Contains(["shrine", "altar"], StringComparison.OrdinalIgnoreCase)
                || edid.ContainsNullable("dlc2standingstone", StringComparison.OrdinalIgnoreCase)
                || rnam.Equals(["pray", "worship"], StringComparison.OrdinalIgnoreCase))
                return ("C", null);
            if (rnam.EqualsNullable("drink", StringComparison.OrdinalIgnoreCase))
                return ("J", null);
            if (rnam.EqualsNullable("eat", StringComparison.OrdinalIgnoreCase))
                return ("K", null);
            if (rnam.Equals(["drop", "place", "exchange"], StringComparison.OrdinalIgnoreCase))
                return ("N", null);
            if (rnam.EqualsNullable("pick up", StringComparison.OrdinalIgnoreCase))
                return ("O", null);
            if (rnam.EqualsNullable("read", StringComparison.OrdinalIgnoreCase))
                return ("P", null);
            if (rnam.EqualsNullable("harvest", StringComparison.OrdinalIgnoreCase))
                return ("Q", null);
            if (rnam.Equals(["take", "catch"], StringComparison.OrdinalIgnoreCase))
                return ("S", null);
            if (rnam.Equals(["talk", "speak"], StringComparison.OrdinalIgnoreCase))
                return ("T", null);
            if (rnam.EqualsNullable("sit", StringComparison.OrdinalIgnoreCase))
                return ("U", null);
            if (rnam.EqualsNullable("open", StringComparison.OrdinalIgnoreCase))
                return ("X", null);
            if (rnam.EqualsNullable("activate", StringComparison.OrdinalIgnoreCase))
                return ("Y", null);
            if (rnam.EqualsNullable("unlock", StringComparison.OrdinalIgnoreCase))
                return ("Z", null);
            if (rnam.EqualsNullable("sleep", StringComparison.OrdinalIgnoreCase)
                || full.Contains(["bed", "hammock", "coffin"], StringComparison.OrdinalIgnoreCase))
                return ("a", null);
            if (edid.ContainsNullable("sconce", StringComparison.OrdinalIgnoreCase))
                return ("i", null);
            if (full.ContainsNullable("keyhole", StringComparison.OrdinalIgnoreCase))
                return ("j", null);
            if (edid.ContainsNullable("cwmap", StringComparison.OrdinalIgnoreCase))
                return ("F", null);
            if (edid.ContainsNullable("ladder", StringComparison.OrdinalIgnoreCase)
                || rnam.Equals(["float", "climb"], StringComparison.OrdinalIgnoreCase))
                return ("d", null);
            if (edid.ContainsNullable("squeeze", StringComparison.OrdinalIgnoreCase))
                return ("e", null);
            if (full.ContainsNullable("fishing supplies", StringComparison.OrdinalIgnoreCase))
                return ("I", null);

            return ("W", null);
        }
    }
}
